// visualiser.cs
//
// Real-time OpenGL visualization using OpenTK for .NET (cross-platform).
// Displays an animated sine wave, simulation time, arbitrary ASCII text, and a 3D wireframe sphere (latitude/longitude lines only).
// Uses modern OpenGL (shaders, VAO/VBO/EBO), a custom 8x8 bitmap font, and modular static classes for clarity and maintainability.
//
// Main features:
//   - Animated sine wave using dynamic VBO updates
//   - Simulation time and arbitrary text rendering with a bitmap font (GL_POINTS)
//   - 3D wireframe sphere (latitude/longitude lines) with aspect-correct rendering
//   - Robust OpenGL resource management and cross-platform compatibility
//   - Modular code: font and text rendering logic in separate static classes
//   - Comprehensive comments for all major sections and logic

using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace lib.graphics;

public class Visualizer
{
    // Entry point for real-time OpenGL visualization with live simulation data
    public static void PlotRealtimeSync(Simulator sim, GuidanceProgram guidance)
    {
        // --- Window setup ---
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1600, 800),
            Title = "Real-time Rocket Trajectory Visualization",
        };

        var gameWindowSettings = new GameWindowSettings()
        {
            UpdateFrequency = 12.0,  // 60 FPS for smooth visualization
        };

        using var window = new GameWindow(gameWindowSettings, nativeWindowSettings);

        // --- OpenGL state variables ---
        int shaderProgram = 0;
        int textShaderProgram = 0;
        int fontAtlasTexture = 0;

        // Vehicle representation (small sphere)
        int vehicleVao = 0, vehicleVbo = 0, vehicleEbo = 0, vehicleIndexCount = 0;

        // Trajectory trail
        int trajVao = 0, trajVbo = 0;
        List<Vector3> trajectoryPoints = new List<Vector3>();

        // Earth sphere
        int earthVao = 0, earthVbo = 0, earthEbo = 0, earthIndexCount = 0;

        // Steering vector
        int steeringVao = 0, steeringVbo = 0;

        // Steering vector
        int keplerVao = 0, keplerVbo = 0;

        // --- OpenGL initialization ---
        window.Load += () =>
        {
            Console.WriteLine($"OpenGL Version: {GL.GetString(StringName.Version)}");
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.ClearColor(0.05f, 0.05f, 0.05f, 1f); // Dark grey space background

            // --- Shader setup ---
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                uniform mat4 transform;
                void main()
                {
                    gl_Position = transform * vec4(aPosition, 1.0);
                }";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec3 color;
                uniform float alpha;
                void main()
                {
                    FragColor = vec4(color, alpha);
                }";

            // Compile and link shaders
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompilation(vertexShader, "vertex");

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompilation(fragmentShader, "fragment");

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);
            CheckProgramLinking(shaderProgram);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // --- Create Earth sphere ---
            CreateSphere(out earthVao, out earthVbo, out earthEbo, out earthIndexCount, 6371000f, 32, 72); // Earth radius in meters

            // --- Create small vehicle sphere ---
            CreateSphere(out vehicleVao, out vehicleVbo, out vehicleEbo, out vehicleIndexCount, 40000f, 8, 6); // Small vehicle representation

            // --- Create trajectory VAO ---
            trajVao = GL.GenVertexArray();
            trajVbo = GL.GenBuffer();
            GL.BindVertexArray(trajVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, trajVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw); // Empty buffer initially
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            // --- Create steering vector VAO ---
            steeringVao = GL.GenVertexArray();
            steeringVbo = GL.GenBuffer();
            GL.BindVertexArray(steeringVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, steeringVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 6 * sizeof(float), new float[6], BufferUsageHint.DynamicDraw); // Two points (line)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            // --- Create kepler orbit VAO ---
            int numpoints = 400;
            keplerVao = GL.GenVertexArray();
            keplerVbo = GL.GenBuffer();
            GL.BindVertexArray(keplerVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, keplerVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, numpoints * sizeof(float), new float[numpoints], BufferUsageHint.DynamicDraw); // Two points (line)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);


            // --- Text shader setup ---
            {
                string textVertexShaderSource = @"
                    #version 330 core
                    layout(location = 0) in vec2 aPosition;
                    layout(location = 1) in vec2 aTexCoord;
                    out vec2 TexCoord;
                    void main() {
                        gl_Position = vec4(aPosition, 0.0, 1.0);
                        TexCoord = aTexCoord;
                    }
                ";
                string textFragmentShaderSource = @"
                    #version 330 core
                    in vec2 TexCoord;
                    out vec4 FragColor;
                    uniform sampler2D fontAtlas;
                    uniform vec3 color;
                    void main() {
                        float alpha = texture(fontAtlas, TexCoord).r;
                        if (alpha < 0.01)
                            discard;
                        FragColor = vec4(color, alpha);
                    }
                ";
                int v = GL.CreateShader(ShaderType.VertexShader);
                GL.ShaderSource(v, textVertexShaderSource);
                GL.CompileShader(v);
                CheckShaderCompilation(v, "text vertex");
                int f = GL.CreateShader(ShaderType.FragmentShader);
                GL.ShaderSource(f, textFragmentShaderSource);
                GL.CompileShader(f);
                CheckShaderCompilation(f, "text fragment");
                textShaderProgram = GL.CreateProgram();
                GL.AttachShader(textShaderProgram, v);
                GL.AttachShader(textShaderProgram, f);
                GL.LinkProgram(textShaderProgram);
                CheckProgramLinking(textShaderProgram);
                GL.DeleteShader(v);
                GL.DeleteShader(f);
            }
            // --- Load bitmap font atlas texture ---
            fontAtlasTexture = BitmapAtlasTextRenderer.LoadFontAtlasTexture("lib/graphics/fonts/B612.png");
        };

        // --- Main render loop ---
        window.RenderFrame += (FrameEventArgs args) =>
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Get current simulation state
            var (position, velocity, time, mass) = Handler.GetCurrentState();
            var trajectoryHistory = Handler.GetTrajectoryHistory();
            var (steering, guidanceMode) = Handler.GetGuidanceInfo();

            // // Exit if simulation is done
            // if (!Handler.IsSimulationRunning())
            // {
            //     window.Close();
            //     return;
            // }

            // --- Camera setup ---
            float aspectRatio = window.Size.X / (float)window.Size.Y;
            Matrix4 projection = Matrix4.CreateTranslation(new Vector3(7000000f, 0f, 0f)) * Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspectRatio, 100f, 50000000f);

            // Camera follows the vehicle at a distance
            Vector3 cameraOffset = new Vector3(0, -6000000f, 0);
            Vector3 cameraPos = (OpenTK.Mathematics.Vector3)position * 3 + cameraOffset;
            Matrix4 view = Matrix4.LookAt(cameraPos, (OpenTK.Mathematics.Vector3)position, Vector3.UnitY);

            GL.UseProgram(shaderProgram);

            // --- Draw Earth ---
            GL.BindVertexArray(earthVao);
            SetUniformColor(shaderProgram, 0.5f, 0.5f, 0.5f, 0.5f); // Blue Earth
            Matrix4 earthTransform = Matrix4.CreateTranslation(Vector3.Zero) * view * projection;
            SetUniformMatrix(shaderProgram, "transform", earthTransform);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.DrawElements(PrimitiveType.Triangles, earthIndexCount, DrawElementsType.UnsignedInt, 0);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

            // --- Draw vehicle ---
            if ((OpenTK.Mathematics.Vector3)position != Vector3.Zero)
            {
                GL.BindVertexArray(vehicleVao);
                SetUniformColor(shaderProgram, 1.0f, 1.0f, 1.0f, 1.0f); // white vehicle
                Matrix4 vehicleTransform = Matrix4.CreateTranslation((OpenTK.Mathematics.Vector3)position) * view * projection;
                SetUniformMatrix(shaderProgram, "transform", vehicleTransform);
                GL.DrawElements(PrimitiveType.Triangles, vehicleIndexCount, DrawElementsType.UnsignedInt, 0);
            }

            // --- Draw trajectory trail ---
            if (trajectoryHistory.Count > 1)
            {
                // Convert trajectory to float array
                float[] trajData = new float[trajectoryHistory.Count * 3];
                for (int i = 0; i < trajectoryHistory.Count; i++)
                {
                    trajData[i * 3] = trajectoryHistory[i].X;
                    trajData[i * 3 + 1] = trajectoryHistory[i].Y;
                    trajData[i * 3 + 2] = trajectoryHistory[i].Z;
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, trajVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, trajData.Length * sizeof(float), trajData, BufferUsageHint.DynamicDraw);

                GL.BindVertexArray(trajVao);
                SetUniformColor(shaderProgram, 0.0f, 1.0f, 0.0f, 0.8f); // green trail
                Matrix4 trajTransform = view * projection;
                SetUniformMatrix(shaderProgram, "transform", trajTransform);
                GL.LineWidth(2.0f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, trajectoryHistory.Count);
            }
            // Draw kepler orbit
            if (trajectoryHistory.Count > 1)
            {
                float[] keplerData = Utils.PlotOrbit(sim.State.Kepler);

                GL.BindBuffer(BufferTarget.ArrayBuffer, keplerVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, keplerData.Length * sizeof(float), keplerData, BufferUsageHint.DynamicDraw);

                GL.BindVertexArray(keplerVao);
                SetUniformColor(shaderProgram, 3.0f, 3.0f, 1.0f, 0.8f); // blue trail
                Matrix4 keplerTransform = view * projection;
                SetUniformMatrix(shaderProgram, "transform", keplerTransform);
                GL.LineWidth(2.0f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, keplerData.Length / 3);
            }

            
            // --- Draw steering vector ---
            if ((OpenTK.Mathematics.Vector3)steering != Vector3.Zero && (OpenTK.Mathematics.Vector3)position != Vector3.Zero)
            {
                Vector3 steeringEnd = (OpenTK.Mathematics.Vector3)position + Vector3.Normalize((OpenTK.Mathematics.Vector3)steering) * 2000000f; // 2000 km long
                float[] steeringData = {
                position.X, position.Y, position.Z,
                steeringEnd.X, steeringEnd.Y, steeringEnd.Z
            };

                GL.BindBuffer(BufferTarget.ArrayBuffer, steeringVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, steeringData.Length * sizeof(float), steeringData, BufferUsageHint.DynamicDraw);

                GL.BindVertexArray(steeringVao);
                SetUniformColor(shaderProgram, 1.0f, 0.0f, 0.0f, 1.0f); // red steering vector
                Matrix4 steeringTransform = view * projection;
                SetUniformMatrix(shaderProgram, "transform", steeringTransform);
                GL.LineWidth(3.0f);
                GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            }

            // --- Draw text information ---
            string info = DrawTextInfo((OpenTK.Mathematics.Vector3)position, (OpenTK.Mathematics.Vector3)velocity, time, mass, guidanceMode);
            BitmapAtlasTextRenderer.DrawText(info, -0.98f, 0.92f, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
            GL.BindVertexArray(0);
            GL.UseProgram(shaderProgram);


  




            // --- Draw actual / target params
            IGuidanceTarget tgt = guidance.Targets[guidance.ActiveMode];
            if (tgt is UPFGTarget upfgTarget)
            {
                // --- Draw upfg params
                info = "TGT PARAMS";
                BitmapAtlasTextRenderer.DrawText(info, -0.98f, 0.86f, 0.04f * 1.7f, 0f, 1f, 0f, textShaderProgram, fontAtlasTexture);
                GL.BindVertexArray(0);
                GL.UseProgram(shaderProgram);
               
                string simvars = Utils.PrintVars(sim, upfgTarget, guidance.Vehicle);

                string[] lines = simvars.Split('\n');
                float y = 0.80f; // Start a bit lower than the top
                float lineSpacing = 0.05f; // Adjust as needed for your font size

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    // Skip lines that are exactly 'Count' or start with 'Count:' (case-insensitive)
                    if (trimmed == "Count" || trimmed.StartsWith("Count:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        BitmapAtlasTextRenderer.DrawText(line, -0.98f, y, 0.04f*0.9f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
                        y -= lineSpacing;
                    }
                }
                
                GL.BindVertexArray(0);
                GL.UseProgram(shaderProgram);
                
            }

            IGuidanceMode mode = guidance.Modes[guidance.ActiveMode];
            if (mode is UpfgMode upfgMode)
            {
                // --- Draw upfg params
                info = "UPFG PARAMS";
                BitmapAtlasTextRenderer.DrawText(info, -0.65f, 0.86f, 0.04f * 1.7f, 0.0f, 1.0f, 0.0f, textShaderProgram, fontAtlasTexture);
                GL.BindVertexArray(0);
                GL.UseProgram(shaderProgram);
                string simvars = Utils.PrintUPFG(upfgMode.upfg, sim);

                string[] lines = simvars.Split('\n');
                float y = 0.80f; // Start a bit lower than the top
                float lineSpacing = 0.05f; // Adjust as needed for your font size

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    // Skip lines that are exactly 'Count' (with or without whitespace)
                    if (trimmed == "Count" || trimmed.StartsWith("Count:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        BitmapAtlasTextRenderer.DrawText(line, -0.65f, y, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
                        y -= lineSpacing;
                    }
                }
                // Console.WriteLine(simvars);
                GL.BindVertexArray(0);
                GL.UseProgram(shaderProgram);

            }

            

            

            window.SwapBuffers();
        };

        window.UpdateFrame += (FrameEventArgs args) =>
        {
            var input = window.KeyboardState;
            if (input.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape))
            {
                window.Close();
            }
        };

        // --- Cleanup ---
        window.Unload += () =>
        {
            GL.DeleteVertexArray(earthVao);
            GL.DeleteBuffer(earthVbo);
            GL.DeleteBuffer(earthEbo);
            GL.DeleteVertexArray(vehicleVao);
            GL.DeleteBuffer(vehicleVbo);
            GL.DeleteBuffer(vehicleEbo);
            GL.DeleteVertexArray(trajVao);
            GL.DeleteBuffer(trajVbo);
            GL.DeleteVertexArray(steeringVao);
            GL.DeleteBuffer(steeringVbo);
            GL.DeleteProgram(shaderProgram);
            GL.DeleteProgram(textShaderProgram);
        };

        window.Run();
    }

    // --- Helper methods ---
    private static void CreateSphere(out int vao, out int vbo, out int ebo, out int indexCount, float radius, int latSegments, int lonSegments)
    {
        List<float> vertices = new List<float>();
        List<uint> indices = new List<uint>();

        // Generate vertices
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = (float)Math.PI * lat / latSegments;
            float y = (float)Math.Cos(theta) * radius;
            float r = (float)Math.Sin(theta) * radius;

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = 2f * (float)Math.PI * lon / lonSegments;
                float x = (float)Math.Cos(phi) * r;
                float z = (float)Math.Sin(phi) * r;
                vertices.AddRange(new float[] { x, y, z });
            }
        }

        // Generate indices
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                uint i0 = (uint)(lat * (lonSegments + 1) + lon);
                uint i1 = i0 + 1;
                uint i2 = i0 + (uint)(lonSegments + 1);
                uint i3 = i2 + 1;

                indices.AddRange(new uint[] { i0, i2, i1, i1, i2, i3 });
            }
        }

        indexCount = indices.Count;

        // Create OpenGL objects
        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    private static Vector3 EciToGl(Vector3d eci)
    {
        // eci.X, eci.Y, eci.Z  -->  gl.Z, gl.X, gl.Y
        return new Vector3((float)eci.Z, (float)eci.X, (float)eci.Y);
    }

    private static void SetUniformColor(int shaderProgram, float r, float g, float b, float a)
    {
        int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
        int alphaLoc = GL.GetUniformLocation(shaderProgram, "alpha");
        if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
        if (alphaLoc >= 0) GL.Uniform1(alphaLoc, a);
    }

    private static void SetUniformMatrix(int shaderProgram, string name, Matrix4 matrix)
    {
        int location = GL.GetUniformLocation(shaderProgram, name);
        if (location >= 0) GL.UniformMatrix4(location, false, ref matrix);
    }

    private static void CheckShaderCompilation(int shader, string type)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            Console.WriteLine($"{type} shader compilation failed: {infoLog}");
        }
    }

    private static void CheckProgramLinking(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = GL.GetProgramInfoLog(program);
            Console.WriteLine($"Shader program linking failed: {infoLog}");
        }
    }

    private static string DrawTextInfo(Vector3 position, Vector3 velocity, double time, float mass, GuidanceMode mode)
    {
        // Convert to more readable units
        float altitude = (position.Length - 6371000f) / 1000f; // Altitude in km
        float speed = velocity.Length / 1000f; // Speed in km/s

        return $"T+{time:F1}s | Alt: {altitude:F1}km | Speed: {speed:F2}km/s | Mass: {mass:F0}kg | Mode: {mode}";
    }
            // Helper: draw text at (x, y) in NDC [-1,1], with scale (pixel size in NDC)
        // Modern OpenGL version: builds a VBO of points and draws with a shader
    private static void DrawText(string text, float x, float y, float scale, float r, float g, float b)
    {
        List<float> points = new();
        float cursorX = x;
        foreach (char c in text)
        {
            if (!BitmapFont8x8.Font.TryGetValue(c, out var glyph))
            {
                cursorX += 9 * scale * 2f / 1200f;
                continue;
            }
            for (int row = 0; row < 8; row++)
            {
                byte rowData = glyph[row];
                for (int col = 0; col < 8; col++)
                {
                    // Fix: flip col index so bits are drawn left-to-right
                    if (((rowData >> col) & 1) != 0)
                    {
                        float px = cursorX + col * scale * 2f / 1200f;
                        float py = y - row * scale * 2f / 800f;
                        points.Add(px);
                        points.Add(py);
                    }
                }
            }
            cursorX += 9 * scale * 2f / 1200f;
        }
    }
}


