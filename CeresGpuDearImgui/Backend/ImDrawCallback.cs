using CeresGpu.Graphics;
using ImGuiNET;

namespace CeresGpuDearImgui.Backend
{
    public delegate void ImDrawCallback(ImDrawListPtr list, ImDrawCmdPtr cmd, ICommandEncoder encoder, ScissorRect scissor, Viewport viewport);
}