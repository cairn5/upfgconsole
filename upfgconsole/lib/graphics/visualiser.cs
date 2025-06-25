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

namespace lib.graphics;

public class Visualizer
{
    // Entry point for real-time OpenGL visualization with live simulation data
    public static void PlotRealtimeSync(Simulator sim, GuidanceProgram guidance)
    {
        // --- Window setup ---
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1200, 800),
            Title = "Real-time Rocket Trajectory Visualization",
        };

        var gameWindowSettings = new GameWindowSettings()
        {
            UpdateFrequency = 60.0,  // 60 FPS for smooth visualization
        };
        
        using var window = new GameWindow(gameWindowSettings, nativeWindowSettings);

        // --- OpenGL state variables ---
        int shaderProgram = 0;
        
        // Vehicle representation (small sphere)
        int vehicleVao = 0, vehicleVbo = 0, vehicleEbo = 0, vehicleIndexCount = 0;
        
        // Trajectory trail
        int trajVao = 0, trajVbo = 0;
        List<Vector3> trajectoryPoints = new List<Vector3>();
        
        // Earth sphere
        int earthVao = 0, earthVbo = 0, earthEbo = 0, earthIndexCount = 0;
        
        // Steering vector
        int steeringVao = 0, steeringVbo = 0;

        // --- OpenGL initialization ---
        window.Load += () =>
        {
            Console.WriteLine($"OpenGL Version: {GL.GetString(StringName.Version)}");
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.ClearColor(0.05f, 0.05f, 0.1f, 1f); // Dark blue space background

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
            CreateSphere(out vehicleVao, out vehicleVbo, out vehicleEbo, out vehicleIndexCount, 500f, 8, 6); // Small vehicle representation
            
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
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspectRatio, 1000f, 50000000f);

            // Camera follows the vehicle at a distance
            // Vector3 cameraOffset = new Vector3(0, 0, 150000f); // 15,000 km behind
            // Vector3 cameraPos = (OpenTK.Mathematics.Vector3)position + cameraOffset;
            // Matrix4 view = Matrix4.LookAt(cameraPos, (OpenTK.Mathematics.Vector3)position, Vector3.UnitY);

            Matrix4 view =  Matrix4.CreateTranslation(new Vector3(0, 0, -30000000));
            GL.UseProgram(shaderProgram);

            // --- Draw Earth ---
            GL.BindVertexArray(earthVao);
            SetUniformColor(shaderProgram, 0.3f, 0.7f, 1.0f, 1.0f); // Blue Earth
            Matrix4 earthTransform = Matrix4.CreateTranslation(Vector3.Zero) * view * projection;
            SetUniformMatrix(shaderProgram, "transform", earthTransform);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.DrawElements(PrimitiveType.Triangles, earthIndexCount, DrawElementsType.UnsignedInt, 0);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

            // --- Draw vehicle ---
            if ((OpenTK.Mathematics.Vector3)position != Vector3.Zero)
            {
                GL.BindVertexArray(vehicleVao);
                SetUniformColor(shaderProgram, 1.0f, 0.5f, 0.0f, 1.0f); // Orange vehicle
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
                SetUniformColor(shaderProgram, 1.0f, 1.0f, 0.0f, 0.8f); // Yellow trail
                Matrix4 trajTransform = view * projection;
                SetUniformMatrix(shaderProgram, "transform", trajTransform);
                GL.LineWidth(2.0f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, trajectoryHistory.Count);
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
                SetUniformColor(shaderProgram, 0.0f, 1.0f, 0.0f, 1.0f); // Green steering vector
                Matrix4 steeringTransform = view * projection;
                SetUniformMatrix(shaderProgram, "transform", steeringTransform);
                GL.LineWidth(3.0f);
                GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            }

            // --- Draw text information ---
            DrawTextInfo((OpenTK.Mathematics.Vector3)position, (OpenTK.Mathematics.Vector3)velocity, time, mass, guidanceMode);

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

    private static void DrawTextInfo(Vector3 position, Vector3 velocity, double time, float mass, GuidanceMode mode)
    {
        // Convert to more readable units
        float altitude = (position.Length - 6371000f) / 1000f; // Altitude in km
        float speed = velocity.Length / 1000f; // Speed in km/s
        
        // For now, just print to console (you can implement overlay text rendering later)
        if ((int)time % 5 == 0) // Print every 5 seconds
        {
            Console.WriteLine($"T+{time:F1}s | Alt: {altitude:F1}km | Speed: {speed:F2}km/s | Mass: {mass:F0}kg | Mode: {mode}");
        }
    }
}

// --- BitmapFont8x8: 8x8 ASCII bitmap font for text rendering ---
// Each character is 8x8 pixels, stored as 8 bytes (one per row, LSB leftmost)
// Covers all printable ASCII characters (32-127)
public static class BitmapFont8x8
{
    // Font dictionary: maps char to 8-byte bitmap
    public static readonly Dictionary<char, byte[]> Font = new()
    {
        [' '] = new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00},
        ['!'] = new byte[]{0x18,0x3C,0x3C,0x18,0x18,0x00,0x18,0x00},
        ['"'] = new byte[]{0x36,0x36,0x24,0x00,0x00,0x00,0x00,0x00},
        ['#'] = new byte[]{0x36,0x36,0x7F,0x36,0x7F,0x36,0x36,0x00},
        ['$'] = new byte[]{0x0C,0x3E,0x03,0x1E,0x30,0x1F,0x0C,0x00},
        ['%'] = new byte[]{0x00,0x63,0x33,0x18,0x0C,0x66,0x63,0x00},
        ['&'] = new byte[]{0x1C,0x36,0x1C,0x6E,0x3B,0x33,0x6E,0x00},
        ['\''] = new byte[]{0x06,0x06,0x0C,0x00,0x00,0x00,0x00,0x00},
        ['('] = new byte[]{0x18,0x0C,0x06,0x06,0x06,0x0C,0x18,0x00},
        [')'] = new byte[]{0x06,0x0C,0x18,0x18,0x18,0x0C,0x06,0x00},
        ['*'] = new byte[]{0x00,0x36,0x1C,0x7F,0x1C,0x36,0x00,0x00},
        ['+'] = new byte[]{0x00,0x08,0x08,0x3E,0x08,0x08,0x00,0x00},
        [','] = new byte[]{0x00,0x00,0x00,0x00,0x18,0x18,0x30,0x00},
        ['-'] = new byte[]{0x00,0x00,0x00,0x7E,0x00,0x00,0x00,0x00},
        ['.'] = new byte[]{0x00,0x00,0x00,0x00,0x00,0x18,0x18,0x00},
        ['/'] = new byte[]{0x00,0x60,0x30,0x18,0x0C,0x06,0x03,0x00},
        ['0'] = new byte[]{0x3E,0x63,0x73,0x7B,0x6F,0x67,0x3E,0x00},
        ['1'] = new byte[]{0x0C,0x0E,0x0F,0x0C,0x0C,0x0C,0x3F,0x00},
        ['2'] = new byte[]{0x1E,0x33,0x30,0x1C,0x06,0x33,0x3F,0x00},
        ['3'] = new byte[]{0x1E,0x33,0x30,0x1C,0x30,0x33,0x1E,0x00},
        ['4'] = new byte[]{0x38,0x3C,0x36,0x33,0x7F,0x30,0x78,0x00},
        ['5'] = new byte[]{0x3F,0x03,0x1F,0x30,0x30,0x33,0x1E,0x00},
        ['6'] = new byte[]{0x1C,0x06,0x03,0x1F,0x33,0x33,0x1E,0x00},
        ['7'] = new byte[]{0x3F,0x33,0x30,0x18,0x0C,0x0C,0x0C,0x00},
        ['8'] = new byte[]{0x1E,0x33,0x33,0x1E,0x33,0x33,0x1E,0x00},
        ['9'] = new byte[]{0x1E,0x33,0x33,0x3E,0x30,0x18,0x0E,0x00},
        [':'] = new byte[]{0x00,0x18,0x18,0x00,0x00,0x18,0x18,0x00},
        [';'] = new byte[]{0x00,0x18,0x18,0x00,0x18,0x18,0x30,0x00},
        ['<'] = new byte[]{0x18,0x0C,0x06,0x03,0x06,0x0C,0x18,0x00},
        ['='] = new byte[]{0x00,0x00,0x7E,0x00,0x7E,0x00,0x00,0x00},
        ['>'] = new byte[]{0x06,0x0C,0x18,0x30,0x18,0x0C,0x06,0x00},
        ['?'] = new byte[]{0x1E,0x33,0x30,0x18,0x0C,0x00,0x0C,0x00},
        ['@'] = new byte[]{0x3E,0x63,0x7B,0x7B,0x7B,0x03,0x1E,0x00},
        ['A'] = new byte[]{0x0C,0x1E,0x33,0x33,0x3F,0x33,0x33,0x00},
        ['B'] = new byte[]{0x3F,0x66,0x66,0x3E,0x66,0x66,0x3F,0x00},
        ['C'] = new byte[]{0x3C,0x66,0x03,0x03,0x03,0x66,0x3C,0x00},
        ['D'] = new byte[]{0x1F,0x36,0x66,0x66,0x66,0x36,0x1F,0x00},
        ['E'] = new byte[]{0x7F,0x46,0x16,0x1E,0x16,0x46,0x7F,0x00},
        ['F'] = new byte[]{0x7F,0x46,0x16,0x1E,0x16,0x06,0x0F,0x00},
        ['G'] = new byte[]{0x3C,0x66,0x03,0x03,0x73,0x66,0x7C,0x00},
        ['H'] = new byte[]{0x33,0x33,0x33,0x3F,0x33,0x33,0x33,0x00},
        ['I'] = new byte[]{0x1E,0x0C,0x0C,0x0C,0x0C,0x0C,0x1E,0x00},
        ['J'] = new byte[]{0x78,0x30,0x30,0x30,0x33,0x33,0x1E,0x00},
        ['K'] = new byte[]{0x67,0x66,0x36,0x1E,0x36,0x66,0x67,0x00},
        ['L'] = new byte[]{0x0F,0x06,0x06,0x06,0x46,0x66,0x7F,0x00},
        ['M'] = new byte[]{0x63,0x77,0x7F,0x7F,0x6B,0x63,0x63,0x00},
        ['N'] = new byte[]{0x63,0x67,0x6F,0x7B,0x73,0x63,0x63,0x00},
        ['O'] = new byte[]{0x1C,0x36,0x63,0x63,0x63,0x36,0x1C,0x00},
        ['P'] = new byte[]{0x3F,0x66,0x66,0x3E,0x06,0x06,0x0F,0x00},
        ['Q'] = new byte[]{0x1E,0x33,0x33,0x33,0x3B,0x1E,0x38,0x00},
        ['R'] = new byte[]{0x3F,0x66,0x66,0x3E,0x36,0x66,0x67,0x00},
        ['S'] = new byte[]{0x1E,0x33,0x07,0x0E,0x38,0x33,0x1E,0x00},
        ['T'] = new byte[]{0x3F,0x2D,0x0C,0x0C,0x0C,0x0C,0x1E,0x00},
        ['U'] = new byte[]{0x33,0x33,0x33,0x33,0x33,0x33,0x3F,0x00},
        ['V'] = new byte[]{0x33,0x33,0x33,0x33,0x33,0x1E,0x0C,0x00},
        ['W'] = new byte[]{0x63,0x63,0x63,0x6B,0x7F,0x77,0x63,0x00},
        ['X'] = new byte[]{0x63,0x63,0x36,0x1C,0x1C,0x36,0x63,0x00},
        ['Y'] = new byte[]{0x33,0x33,0x33,0x1E,0x0C,0x0C,0x1E,0x00},
        ['Z'] = new byte[]{0x7F,0x73,0x19,0x0C,0x26,0x67,0x7F,0x00},
        ['['] = new byte[]{0x1E,0x06,0x06,0x06,0x06,0x06,0x1E,0x00},
        ['\\'] = new byte[]{0x03,0x06,0x0C,0x18,0x30,0x60,0x40,0x00},
        [']'] = new byte[]{0x1E,0x18,0x18,0x18,0x18,0x18,0x1E,0x00},
        ['^'] = new byte[]{0x08,0x1C,0x36,0x63,0x00,0x00,0x00,0x00},
        ['_'] = new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xFF},
        ['`'] = new byte[]{0x0C,0x0C,0x18,0x00,0x00,0x00,0x00,0x00},
        ['a'] = new byte[]{0x00,0x00,0x1E,0x30,0x3E,0x33,0x6E,0x00},
        ['b'] = new byte[]{0x07,0x06,0x06,0x3E,0x66,0x66,0x3B,0x00},
        ['c'] = new byte[]{0x00,0x00,0x1E,0x33,0x03,0x33,0x1E,0x00},
        ['d'] = new byte[]{0x38,0x30,0x30,0x3E,0x33,0x33,0x6E,0x00},
        ['e'] = new byte[]{0x00,0x00,0x1E,0x33,0x3F,0x03,0x1E,0x00},
        ['f'] = new byte[]{0x1C,0x36,0x06,0x0F,0x06,0x06,0x0F,0x00},
        ['g'] = new byte[]{0x00,0x00,0x6E,0x33,0x33,0x3E,0x30,0x1F},
        ['h'] = new byte[]{0x07,0x06,0x36,0x6E,0x66,0x66,0x67,0x00},
        ['i'] = new byte[]{0x0C,0x00,0x0E,0x0C,0x0C,0x0C,0x1E,0x00},
        ['j'] = new byte[]{0x30,0x00,0x38,0x30,0x30,0x33,0x33,0x1E},
        ['k'] = new byte[]{0x07,0x06,0x66,0x36,0x1E,0x36,0x67,0x00},
        ['l'] = new byte[]{0x0E,0x0C,0x0C,0x0C,0x0C,0x0C,0x1E,0x00},
        ['m'] = new byte[]{0x00,0x00,0x33,0x7F,0x7F,0x6B,0x63,0x00},
        ['n'] = new byte[]{0x00,0x00,0x1B,0x37,0x33,0x33,0x33,0x00},
        ['o'] = new byte[]{0x00,0x00,0x1E,0x33,0x33,0x33,0x1E,0x00},
        ['p'] = new byte[]{0x00,0x00,0x3B,0x66,0x66,0x3E,0x06,0x0F},
        ['q'] = new byte[]{0x00,0x00,0x6E,0x33,0x33,0x3E,0x30,0x78},
        ['r'] = new byte[]{0x00,0x00,0x3B,0x6E,0x66,0x06,0x0F,0x00},
        ['s'] = new byte[]{0x00,0x00,0x3E,0x03,0x1E,0x30,0x1F,0x00},
        ['t'] = new byte[]{0x08,0x0C,0x3E,0x0C,0x0C,0x2C,0x18,0x00},
        ['u'] = new byte[]{0x00,0x00,0x33,0x33,0x33,0x33,0x6E,0x00},
        ['v'] = new byte[]{0x00,0x00,0x33,0x33,0x33,0x1E,0x0C,0x00},
        ['w'] = new byte[]{0x00,0x00,0x63,0x6B,0x7F,0x7F,0x36,0x00},
        ['x'] = new byte[]{0x00,0x00,0x63,0x36,0x1C,0x36,0x63,0x00},
        ['y'] = new byte[]{0x00,0x00,0x33,0x33,0x33,0x3E,0x30,0x1F},
        ['z'] = new byte[]{0x00,0x00,0x3F,0x19,0x0C,0x26,0x3F,0x00},
        ['{'] = new byte[]{0x38,0x0C,0x0C,0x07,0x0C,0x0C,0x38,0x00},
        ['|'] = new byte[]{0x18,0x18,0x18,0x00,0x18,0x18,0x18,0x00},
        ['}'] = new byte[]{0x07,0x0C,0x0C,0x38,0x0C,0x0C,0x07,0x00},
        ['~'] = new byte[]{0x6E,0x3B,0x00,0x00,0x00,0x00,0x00,0x00},
    };
}

// --- TextRenderer: modular static class for drawing text using BitmapFont8x8 ---
// Provides a reusable DrawText method for rendering ASCII text as GL_POINTS
public static class TextRenderer
{
    // DrawText: draws ASCII text using the 8x8 bitmap font at the given NDC position, scale, and color
    // Arguments:
    //   text           - The string to render (ASCII, printable)
    //   x, y           - NDC coordinates for the start of the text (top-left)
    //   scale          - Point size (pixel size of each font dot)
    //   r, g, b        - Color (0-1)
    //   shaderProgram  - OpenGL shader program to use (must have 'color' uniform)
    public static void DrawText(string text, float x, float y, float scale, float r, float g, float b, int shaderProgram)
    {
        var font8x8 = BitmapFont8x8.Font;
        List<float> points = new();
        float cursorX = x;
        foreach (char c in text)
        {
            if (!font8x8.TryGetValue(c, out var glyph))
            {
                cursorX += 9 * scale * 2f / 1200f;
                continue;
            }
            for (int row = 0; row < 8; row++)
            {
                byte rowData = glyph[row];
                for (int col = 0; col < 8; col++)
                {
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
        if (points.Count == 0) return;
        int textVboTmp = GL.GenBuffer();
        int textVaoTmp = GL.GenVertexArray();
        GL.BindVertexArray(textVaoTmp);
        GL.BindBuffer(BufferTarget.ArrayBuffer, textVboTmp);
        GL.BufferData(BufferTarget.ArrayBuffer, points.Count * sizeof(float), points.ToArray(), BufferUsageHint.StreamDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.UseProgram(shaderProgram);
        int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
        if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
        GL.PointSize(scale);
        GL.DrawArrays(PrimitiveType.Points, 0, points.Count / 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        GL.DeleteBuffer(textVboTmp);
        GL.DeleteVertexArray(textVaoTmp);
    }
}
