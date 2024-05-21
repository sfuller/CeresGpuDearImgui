using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CeresGpuDearImgui.Backend;
using ImGuiNET;
using CeresGpu.Graphics;
using CeresGpu.Graphics.Shaders;
using WizKid.Imgui;

namespace Metalancer.ImGuiIntegration
{
    public sealed class ImGuiRenderer : IDisposable
    {
        private readonly IRenderer _renderer;
        
        private bool _disposed;
        private IntPtr _backendName;

        private readonly IPipeline<ImGuiShader> _pipeline;
        private readonly ImGuiShader _shader;
        private readonly IBuffer<ImGuiShader.VertUniforms> _uniformBuffer;
        private ITexture _texture;
        private ISampler _textureSampler;

        private ITexture _nullTexture;

        public ImGuiRenderer(IRenderer renderer, ShaderManager shaderManager, ISampler textureSampler)
        {
            _renderer = renderer;
            _backendName = Marshal.StringToHGlobalAnsi("imgui_impl_opengl3_ceres");
            
            _shader = shaderManager.GetShader<ImGuiShader>();

            PipelineDefinition pd = new();
            
            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
            pd.Blend = true;
            pd.BlendEquation = BlendEquation.FUNC_ADD;
            pd.BlendFunction.SourceRGB = BlendingFactor.SRC_ALPHA;
            pd.BlendFunction.DestinationRGB = BlendingFactor.ONE_MINUS_SRC_ALPHA;
            pd.BlendFunction.SourceAlpha = BlendingFactor.ONE;
            pd.BlendFunction.DestinationAlpha = BlendingFactor.ONE_MINUS_SRC_ALPHA;
            pd.CullMode = CullMode.None;
            pd.DepthStencil.DepthCompareFunction = CompareFunction.Always;
            pd.DepthStencil.DepthWriteEnabled = false;
            pd.DepthStencil.FrontFaceStencil.StencilCompareFunction = CompareFunction.Always;
            
            //pd = PipelineUtil.StandardPipeline;
            _pipeline = renderer.CreatePipeline(pd, _shader);
            _uniformBuffer = renderer.CreateStreamingBuffer<ImGuiShader.VertUniforms>(1);
            _texture = CreateFontsTexture(renderer);
            _textureSampler = textureSampler;

            // gl.Enable(EnableCap.SCISSOR_TEST);
            // if (GlVersion >= 310) {
            //     gl.Disable(EnableCap.PRIMITIVE_RESTART);
            // }

            _nullTexture = renderer.CreateTexture();
            _nullTexture.Set(new byte[4], 1, 1, InputFormat.R8G8B8A8_UNORM);
        }
        
        //public ImGuiRenderer(ImGuiIOPtr io, GL gl, string? glsl_version)
        public void Setup(ImGuiIOPtr io /* glsl version = 410 */)
        {
            CheckDisposed();
            
            // Setup backend capabilities flags
            // _handle = GCHandle.Alloc(this, GCHandleType.Normal);
            // io.BackendRendererUserData = GCHandle.ToIntPtr(_handle);
            //io.BackendRendererName = "imgui_impl_opengl3";
            
            unsafe {
                io.NativePtr->BackendRendererName = (byte*)_backendName;
            }

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;  // We can honor the ImDrawCmd::VtxOffset field, allowing for large meshes.
        }
        
        public void Dispose()
        {
            CheckDisposed();
            
            _pipeline.Dispose();
            // _shaderInstance.Dispose();
            // _vertexBuffer.Dispose();
            // _indexBuffer.Dispose();
            _uniformBuffer.Dispose();
            _texture.Dispose();

            //ReleaseUnmanagedResources();
            //GC.SuppressFinalize(this);
            _disposed = true;
        }

        private void CheckDisposed()
        {
            if (_disposed) {
                throw new ObjectDisposedException(null);
            }
        }

        private void UpdateUniforms(in ImDrawDataPtr draw_data)
        {
            float L = draw_data.DisplayPos.X;
            float R = draw_data.DisplayPos.X + draw_data.DisplaySize.X;
            float T = draw_data.DisplayPos.Y;
            float B = draw_data.DisplayPos.Y + draw_data.DisplaySize.Y;
            // #if defined(GL_CLIP_ORIGIN)
            //     if (!clip_origin_lower_left) { float tmp = T; T = B; B = tmp; } // Swap top and bottom if origin is upper left
            // #endif
            // Span<float> ortho_projection = stackalloc []
            // {
            //     2.0f/(R-L),   0.0f,         0.0f,   0.0f,
            //     0.0f,         2.0f/(T-B),   0.0f,   0.0f,
            //     0.0f,         0.0f,        -1.0f,   0.0f,
            //     (R+L)/(L-R),  (T+B)/(B-T),  0.0f,   1.0f,
            // };
            
            _uniformBuffer.Set(new ImGuiShader.VertUniforms {
                ProjMtx = new Matrix4x4(
                    2.0f/(R-L),   0.0f,         0.0f,   0.0f,
                    0.0f,         2.0f/(T-B),   0.0f,   0.0f,
                    0.0f,         0.0f,         1.0f,   0.0f,
                    (R+L)/(L-R),  (T+B)/(B-T),  0.0f,   1.0f
                )
            });
        }
        
        private void SetupRenderState(ICommandEncoder encoder, ImGuiShader.Instance shaderInstance, int fb_width, int fb_height)
        {
            // Setup viewport, orthographic projection matrix
            // Our visible imgui space lies from draw_data->DisplayPos (top left) to draw_data->DisplayPos+data_data->DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
            encoder.SetViewport(new Viewport(0, 0, (uint)fb_width, (uint)fb_height));
            
            encoder.SetPipeline(_pipeline, shaderInstance);
        }


        private class Pool
        {
            public readonly Stack<IBuffer<ImGuiShader.Vertex>> VertexBuffers = new();
            public readonly Stack<IBuffer<ushort>> IndexBuffers = new();
            public readonly Stack<ImGuiShader.Instance> ShaderInstances = new();    
        }

        private Pool _unusedPool = new();
        private Pool _usedPool = new();

        private IBuffer<ImGuiShader.Vertex> GetVertexBuffer(int elementCount)
        {
            if (!_unusedPool.VertexBuffers.TryPop(out IBuffer<ImGuiShader.Vertex>? buffer)) {
                buffer = _renderer.CreateStreamingBuffer<ImGuiShader.Vertex>(elementCount);
            } else {
                if (buffer.Count < elementCount) {
                    buffer.Allocate((uint)elementCount);    
                }
            }
            
            _usedPool.VertexBuffers.Push(buffer);
            return buffer;
        }

        private IBuffer<ushort> GetIndexBuffer(int elementCount)
        {
            if (!_unusedPool.IndexBuffers.TryPop(out IBuffer<ushort>? buffer)) {
                buffer = _renderer.CreateStreamingBuffer<ushort>(elementCount);
            } else {
                if (buffer.Count < elementCount) {
                    buffer.Allocate((uint)elementCount);    
                }
            }
            
            _usedPool.IndexBuffers.Push(buffer);
            return buffer;
        }

        private ImGuiShader.Instance GetShaderInstance()
        {
            if (!_unusedPool.ShaderInstances.TryPop(out ImGuiShader.Instance? shaderInstance)) {
                shaderInstance = new ImGuiShader.Instance(_renderer, _shader);
            }
            
            _usedPool.ShaderInstances.Push(shaderInstance);
            return shaderInstance;
        }

        public void RenderDrawData(ICommandEncoder encoder, in ImDrawDataPtr draw_data)
        {
            CheckDisposed();
            
            // Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
            int fb_width = (int)(draw_data.DisplaySize.X * draw_data.FramebufferScale.X);
            int fb_height = (int)(draw_data.DisplaySize.Y * draw_data.FramebufferScale.Y);
            if (fb_width <= 0 || fb_height <= 0) {
                return;
            }
            
            Viewport lastViewport = encoder.CurrentDynamicViewport;
            ScissorRect lastScissor = encoder.CurrentDynamicScissor;

            UpdateUniforms(in draw_data);
            
            //SetupRenderState(encoder, draw_data, fb_width, fb_height);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clip_off = draw_data.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clip_scale = draw_data.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            for (int n = 0; n < draw_data.CmdListsCount; n++) {
                ImDrawListPtr cmd_list = draw_data.CmdLists[n];

                // Upload vertex/index buffers
                IBuffer<ImGuiShader.Vertex> vertexBuffer = GetVertexBuffer(cmd_list.VtxBuffer.Size);
                IBuffer<ushort> indexBuffer = GetIndexBuffer(cmd_list.IdxBuffer.Size);
                
                // if (_vertexBuffer.Count < cmd_list.VtxBuffer.Size) {
                //     _vertexBuffer.Allocate((uint) cmd_list.VtxBuffer.Size);
                // }
                // if (_indexBuffer.Count < cmd_list.IdxBuffer.Size) {
                //     _indexBuffer.Allocate((uint) cmd_list.IdxBuffer.Size);
                // }
                
                unsafe {
                    Span<ImGuiShader.Vertex> vtxData = new Span<ImGuiShader.Vertex>((void*)cmd_list.VtxBuffer.Data, cmd_list.VtxBuffer.Size);
                    Span<ushort> idxData = new Span<ushort>((void*)cmd_list.IdxBuffer.Data, cmd_list.IdxBuffer.Size);
                    vertexBuffer.Set(vtxData);
                    indexBuffer.Set(idxData);
                }
                
                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++) {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    //ImDrawCmd* pcmd = (ImDrawCmd*)cmd_list.CmdBuffer.Address<ImDrawCmd>(cmd_i); 
                    // ref ImDrawCmd pcmd = ref cmd_list->CmdBuffer.Ref<ImDrawCmd>(cmd_i);
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        // User callback, registered via ImDrawList::AddCallback()
                        // (ImDrawCallback_ResetRenderState is a special callback value used by the user to request the renderer to reset render state.)
                        //if (pcmd.UserCallback == ImDrawCallback_ResetRenderState)
                        if (pcmd.UserCallback == new IntPtr(-1)) {
                            //SetupRenderState(encoder, draw_data, fb_width, fb_height);
                        } else {
                            int flags = pcmd.UserCallbackData.ToInt32();
                            if (flags > 0) {
                                Vector4 coords = Vector4.Zero;
                                if (!GetGlClipCoordinates(clip_off, clip_scale, pcmd.ClipRect, fb_width, fb_height, ref coords)) {
                                    continue;
                                }
                                
                                if ((flags & 0b1) > 0) {
                                    encoder.SetScissor(new ScissorRect((int)coords.X, (int)coords.Y, (uint)coords.Z, (uint)coords.W));
                                    //gl.Scissor((int)coords.X, (int)coords.Y, (int)coords.Z, (int)coords.W);
                                }
                                if ((flags & 0b10) > 0) {
                                    encoder.SetViewport(new Viewport((uint)coords.X, (uint)coords.Y, (uint)coords.Z, (uint)coords.W));
                                    //gl.Viewport((int)coords.X, (int)coords.Y, (int)coords.Z, (int)coords.W);
                                }
                            }

                            // TODO: Is GCHandle slow? If so, use c# 9's delegate* feature!
                            ImDrawCallback? callback = GCHandle.FromIntPtr(pcmd.UserCallback).Target as ImDrawCallback;
                            callback?.Invoke(cmd_list, pcmd, encoder);
                        }
                    }
                    else {
                        Vector4 coords = Vector4.Zero;
                        if (!GetGlClipCoordinates(clip_off, clip_scale, pcmd.ClipRect, fb_width, fb_height, ref coords)) {
                            continue;
                        }
                        
                        encoder.SetScissor(new ScissorRect((int)coords.X, (int)coords.Y, (uint)coords.Z, (uint)coords.W));

                        ImGuiShader.Instance shaderInstance = GetShaderInstance();
                        shaderInstance.SetVertex(vertexBuffer);
                        shaderInstance.SetVertUniforms(_uniformBuffer);
                        shaderInstance.SetTextureSampler(_textureSampler);

                        // TODO: USE A PLACEHOLDER TEXTURE WHEN WEAK TEXTURE FROM HANDLE IS NO LONGER AVAILABLE
                        if (GCHandle.FromIntPtr(pcmd.TextureId).Target is ITexture texture) {
                            if (texture.Width == 0 || texture.Height == 0) {
                                texture = _nullTexture;
                            }
                            shaderInstance.SetTexture(texture);
                        } else {
                            shaderInstance.SetTexture(_nullTexture);
                        }
                        
                        SetupRenderState(encoder, shaderInstance, fb_width, fb_height);

                        encoder.DrawIndexedUshort(
                            indexBuffer: indexBuffer,
                            indexCount: pcmd.ElemCount,
                            instanceCount: 1,
                            firstIndex: pcmd.IdxOffset,
                            vertexOffset: pcmd.VtxOffset,
                            firstInstance: 0
                        );
                    }
                }
            }
            
            // TODO: Is this necesary?
            // TODO
            //encoder.SetViewport(lastViewport);
            //encoder.SetScissor(lastScissor);
            
            // Swap the pools
            // TODO: This uses a lot of memory since unused resources left in the pool will be unusable next frame.
            (_usedPool, _unusedPool) = (_unusedPool, _usedPool);
        }

        private bool GetGlClipCoordinates(Vector2 clip_off, Vector2 clip_scale, Vector4 clipRect, int fb_width, int fb_height, ref Vector4 coords)
        {
            // Project scissor/clipping rectangles into framebuffer space
            Vector2 clip_min = new Vector2((clipRect.X - clip_off.X) * clip_scale.X, (clipRect.Y - clip_off.Y) * clip_scale.Y);
            Vector2 clip_max = new Vector2((clipRect.Z - clip_off.X) * clip_scale.X, (clipRect.W - clip_off.Y) * clip_scale.Y);
            
            if (clip_min.X < 0.0f) { clip_min.X = 0.0f; }
            if (clip_min.Y < 0.0f) { clip_min.Y = 0.0f; }
            if (clip_max.X > fb_width) { clip_max.X = fb_width; }
            if (clip_max.Y > fb_height) { clip_max.Y = fb_height; }
            if (clip_max.X <= clip_min.X || clip_max.Y <= clip_min.Y) {
                return false;
            }

            // Apply scissor/clipping rectangle (Y is inverted in OpenGL)
            coords = new Vector4(clip_min.X, clip_min.Y, clip_max.X - clip_min.X, clip_max.Y - clip_min.Y);
            return true;
        }

        private ITexture CreateFontsTexture(IRenderer renderer)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            // ImGuiIO& io = ImGui::GetIO();
            // ImGui_ImplOpenGL3_Data* bd = ImGui_ImplOpenGL3_GetBackendData();

            // Build texture atlas
            Span<byte> pixels;
            int width, height;
            unsafe {
                byte* pixelsPtr;
                int bytes_per_pixel;
                // Load as RGBA 32-bit (75% of the memory is wasted, but default font is so small) because it is more likely to be compatible with user's existing shaders. If your ImTextureId represent a higher-level concept than just a GL texture id, consider calling GetTexDataAsAlpha8() instead to save on GPU memory.
                io.Fonts.GetTexDataAsRGBA32(out pixelsPtr, out width, out height, out bytes_per_pixel);
                pixels = new Span<byte>(pixelsPtr, width * height * bytes_per_pixel);
            }

            // Upload texture to graphics system
            ITexture texture = renderer.CreateTexture();
            texture.Set(pixels, (uint)width, (uint)height, InputFormat.R8G8B8A8_UNORM);

            // Store our identifier
            IntPtr handle = GCHandle.ToIntPtr(GCHandle.Alloc(texture, GCHandleType.Weak));
            io.Fonts.SetTexID(handle);

            return texture;
        }
        
    }
}