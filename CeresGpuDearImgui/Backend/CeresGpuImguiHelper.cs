using System;
using CeresGLFW;
using ImGuiNET;
using Metalancer;
using CeresGpu.Graphics;
using CeresGpu.Graphics.Shaders;
using Metalancer.ImGuiIntegration;

namespace CeresGpuDearImgui.Backend
{
    /// <summary>
    /// Optional class for integrating Imgui into a CeresGPU application.
    /// </summary>
    public sealed class CeresGpuImguiHelper : IDisposable
    {
        private readonly IntPtr _imguiContext;
        private readonly ImGuiBackend _imguiBackend;
        private readonly ImGuiRenderer _imguiRenderer;

        public CeresGpuImguiHelper(IRenderer renderer, GLFWWindow window, ShaderManager shaderManager)
        {
            _imguiContext = ImGui.CreateContext();
            ImGuiIOPtr io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            
            ImGui.StyleColorsDark();

            _imguiBackend = new ImGuiBackend(window, true, io);
            _imguiRenderer = new ImGuiRenderer(renderer, shaderManager);
            
            _imguiRenderer.Setup(ImGui.GetIO());
        }

        private void ReleaseUnmanagedResources()
        {
            ImGui.DestroyContext(_imguiContext);
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing) {
                _imguiBackend.Dispose();
                _imguiRenderer.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CeresGpuImguiHelper() {
            Dispose(false);
        }
        
        public void NewFrame()
        {
            _imguiBackend.NewFrame();
            ImGui.NewFrame();
        }

        public void Render(IPass pass)
        {
            ImGui.Render();
            _imguiRenderer.RenderDrawData(pass, ImGui.GetDrawData());
        }
    }
}