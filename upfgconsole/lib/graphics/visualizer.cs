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

public partial class Visualizer
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

        // kepler orbit 
        int keplerVao = 0, keplerVbo = 0;

        // overlay vao
        int overlayVao = 0, overlayVbo = 0;

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

            //create overlay vao
            overlayVao = GL.GenVertexArray();
            overlayVbo = GL.GenBuffer();
            GL.BindVertexArray(overlayVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw); // Empty buffer initially
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

            float aspectRatio = window.Size.X / (float)window.Size.Y;
            Matrix4 projection = Matrix4.CreateTranslation(new Vector3(7000000f, 0f, 0f)) * Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspectRatio, 100f, 50000000f);
            Vector3 cameraOffset = new Vector3(0, -6000000f, 0);
            Vector3 cameraPos = (Vector3)position * 3 + cameraOffset;
            Matrix4 view = Matrix4.LookAt(cameraPos, (Vector3)position, Vector3.UnitY);

            DrawEarth(shaderProgram, earthVao, earthIndexCount, view, projection);
            DrawVehicle(shaderProgram, vehicleVao, vehicleIndexCount, (Vector3)position, view, projection);
            DrawTrajectoryTrail(shaderProgram, trajVao, trajVbo, trajectoryHistory.Select(v => new Vector3(v.X, v.Y, v.Z)).ToList(), view, projection);
            DrawKeplerOrbit(shaderProgram, keplerVao, keplerVbo, sim, view, projection);
            DrawSteeringVector(shaderProgram, steeringVao, steeringVbo, (Vector3)steering, (Vector3)position, view, projection);
            DrawTextInfoOverlay(textShaderProgram, fontAtlasTexture, (Vector3)position, (Vector3)velocity, time, mass, guidanceMode);
            DrawTargetParams(guidance, sim, textShaderProgram, fontAtlasTexture);
            DrawUpfgParams(guidance, sim, textShaderProgram, fontAtlasTexture);

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

}