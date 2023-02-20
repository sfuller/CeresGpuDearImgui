using System.Collections.Generic;
using ImGuiNET;
using CeresGpu.Graphics;

namespace CeresGpuDearImgui.Util;

public static class DiagnosticInfoGui
{
    private static List<(string key, object info)> _info = new();
    
    public static void DoImgui(IRenderer renderer, ref bool isOpen)
    {
        if (!isOpen) {
            return;
        }
        
        if (ImGui.Begin("Ceres GPU Diagnostic Info", ref isOpen)) {
            _info.Clear();
            renderer.GetDiagnosticInfo(_info);

            ImGuiTableFlags flags = ImGuiTableFlags.RowBg;
            if (ImGui.BeginTable("DiagnosticInfoTable", 2, flags)) {
                foreach ((string key, object value) in _info) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(key);
                    ImGui.TableNextColumn();
                    ImGui.Text(value.ToString());
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }
    
}