// #CSNAME:CeresGpuDearImgui.Backend.ImGuiShader
// #CSFIELD:Vertex
#version 450

layout (location = 0) in vec2 Position; 
layout (location = 1) in vec2 UV;
layout (location = 2) in uint Color; 

// TODO: Push constants?
layout (set = 0, binding = 0) uniform VertUniforms {
    mat4 ProjMtx;
} u;

layout(location = 0) out vec2 Frag_UV;
layout(location = 1) out vec4 Frag_Color;

void main()
{
    Frag_UV = UV;
    
    float a = ((Color >> 24) & 0xFF) / 255.0; 
    float b = ((Color >> 16) & 0xFF) / 255.0; 
    float g = ((Color >> 8) & 0xFF) / 255.0;
    float r = (Color & 0xFF) / 255.0; 
    Frag_Color = vec4(r,g,b,a);
     
    gl_Position = u.ProjMtx * vec4(Position.xy,0,1);
}