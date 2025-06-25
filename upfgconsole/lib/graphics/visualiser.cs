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
    // Entry point for real-time OpenGL visualization.
    // Opens an OpenTK window and renders the animated scene (sine wave, text, sphere).
    public static void PlotRealtimeSync(Simulator sim, GuidanceProgram guidance)
    {
        // --- Window setup ---
        // Configure window size and title for the OpenTK GameWindow.
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1200, 800),
            Title = "OpenTK Modern Shader Sine Wave Demo",
        };


        // Create the OpenTK GameWindow (main OpenGL context and event loop).
        using var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings);

        // --- Sine wave state ---
        int shaderProgram = 0, vao = 0, vbo = 0; // OpenGL handles for sine wave
        int numPoints = 400; // Number of points in the sine wave
        float[] vertices = new float[numPoints * 2]; // (x, y) pairs for sine wave
        uint[] indices = new uint[numPoints]; // Indices for line strip
        double phase = 0; // Phase offset for animation
        double speed = 0.5 * Math.PI / 2; // Sine wave speed (1 period every 2 seconds)

        // --- Text rendering state ---
        int textVao = 0, textVbo = 0; // VAO/VBO for text quad (not used in GL_POINTS font rendering)
        int textShader = 0; // Shader for text (not used in GL_POINTS font rendering)

        // --- Sphere mesh state (must be accessible in RenderFrame and Unload) ---
        int sphereIndexCount = 0; // Number of indices for sphere mesh
        int sphereVao = 0, sphereVbo = 0, sphereEbo = 0; // OpenGL handles for sphere

        // --- (Legacy) Textured quad shader sources (not used for GL_POINTS font) ---
        string textVertexShaderSrc = "#version 330 core\nlayout(location=0) in vec2 aPos;layout(location=1) in vec2 aTex;out vec2 TexCoord;void main(){gl_Position=vec4(aPos,0,1);TexCoord=aTex;}";
        string textFragmentShaderSrc = "#version 330 core\nin vec2 TexCoord;out vec4 FragColor;uniform sampler2D tex;void main(){FragColor = texture(tex,TexCoord);}";

        // --- OpenGL resource and mesh initialization ---
        window.Load += () =>
        {
            // Print OpenGL version for debugging
            Console.WriteLine($"OpenGL Version: {GL.GetString(StringName.Version)}");
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);

            // --- Shader setup (modern OpenGL, core profile) ---
            // Vertex shader: accepts 3D position, applies transform matrix
            string vertexShaderSource = "#version 330 core\nlayout(location = 0) in vec3 aPosition;\nuniform mat4 transform;\nvoid main(){gl_Position = transform * vec4(aPosition, 1.0) ;}";
            // Fragment shader: outputs solid color from uniform
            string fragmentShaderSource = "#version 330 core\nout vec4 FragColor;\nuniform vec3 color;\nvoid main(){FragColor = vec4(color, 1.0);}";
            // Compile vertex shader
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            // Compile fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            // Link shaders into a program
            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);

            // Clean up shader objects (no longer needed after linking)
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // --- Sine wave VAO/VBO/EBO setup ---
            // Prepare index buffer for line strip
            for (uint i = 0; i < numPoints; i++) indices[i] = i;
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            // Set background color (dark gray)
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1f);

            // --- (Legacy) Text shader and quad setup (not used for GL_POINTS font) ---
            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, textVertexShaderSrc);
            GL.CompileShader(v);
            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, textFragmentShaderSrc);
            GL.CompileShader(f);
            textShader = GL.CreateProgram();
            GL.AttachShader(textShader, v);
            GL.AttachShader(textShader, f);
            GL.LinkProgram(textShader);
            GL.DeleteShader(v);
            GL.DeleteShader(f);
            // Text quad VAO/VBO (not used for GL_POINTS font, but left for reference)
            float[] quad = {
                -1f,  1f, 0f, 0f,
                -0.6f, 1f, 1f, 0f,
                -0.6f, 0.9f, 1f, 1f,
                -1f,  0.9f, 0f, 1f
            };
            uint[] quadIdx = { 0, 1, 2, 2, 3, 0 };
            textVao = GL.GenVertexArray();
            textVbo = GL.GenBuffer();
            int textEbo = GL.GenBuffer();
            GL.BindVertexArray(textVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, textVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quad.Length * sizeof(float), quad, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, textEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, quadIdx.Length * sizeof(uint), quadIdx, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            // --- Sphere mesh generation (latitude/longitude lines) ---
            // Generate vertices and indices for a 3D sphere mesh (for wireframe rendering)
            List<float> sphereVertices = new();
            List<uint> sphereIndices = new();
            int sphereLat = 18, sphereLon = 36; // Sphere resolution (latitude/longitude divisions)
            float sphereRadius = 0.5f; // Sphere radius (NDC units)
            for (int lat = 0; lat <= sphereLat; lat++)
            {
                float theta = (float)Math.PI * lat / sphereLat;
                float y = (float)Math.Cos(theta) * sphereRadius;
                float r = (float)Math.Sin(theta) * sphereRadius;
                for (int lon = 0; lon <= sphereLon; lon++)
                {
                    float phi = 2f * (float)Math.PI * lon / sphereLon;
                    float x = (float)Math.Cos(phi) * r;
                    float z = (float)Math.Sin(phi) * r;
                    // Store (x, y, z) for each sphere vertex
                    sphereVertices.Add(x);
                    sphereVertices.Add(y);
                    sphereVertices.Add(z);
                }
            }
            // Generate indices for sphere triangles (for wireframe rendering)
            for (int lat = 0; lat < sphereLat; lat++)
            {
                for (int lon = 0; lon < sphereLon; lon++)
                {
                    int i0 = lat * (sphereLon + 1) + lon;
                    int i1 = i0 + 1;
                    int i2 = i0 + (sphereLon + 1);
                    int i3 = i2 + 1;
                    // Two triangles per quad (for full mesh, but drawn as wireframe)
                    sphereIndices.Add((uint)i0);
                    sphereIndices.Add((uint)i2);
                    sphereIndices.Add((uint)i1);
                    sphereIndices.Add((uint)i1);
                    sphereIndices.Add((uint)i2);
                    sphereIndices.Add((uint)i3);
                }
            }
            sphereIndexCount = sphereIndices.Count;
            // Sphere VAO/VBO/EBO setup (3 floats per vertex)
            sphereVao = GL.GenVertexArray();
            sphereVbo = GL.GenBuffer();
            sphereEbo = GL.GenBuffer();
            GL.BindVertexArray(sphereVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, sphereVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sphereVertices.Count * sizeof(float), sphereVertices.ToArray(), BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, sphereEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, sphereIndices.Count * sizeof(uint), sphereIndices.ToArray(), BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        };


        // --- Main render loop: called every frame ---
        window.RenderFrame += (FrameEventArgs args) =>
        {

            float aspectRatio = window.Size.X / (float)window.Size.Y;
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(80f), 
                aspectRatio, 
                0.1f, 
                200.0f
            );

            // Position camera looking at origin from positive Z
            // Matrix4 view = Matrix4.LookAt(
            //     new Vector3(0f, 0f, 0f),  // Camera position
            //     new Vector3(0f, 0f, 0f),             // Look at origin
            //     Vector3.UnitY            // Up vector
            // );



            // --- Animate sine wave ---
            phase += speed * args.Time; // Advance phase for animation
            for (int i = 0; i < numPoints; i++)
            {
                float x = (float)i / (numPoints - 1) * 2f - 1f; // NDC X: -1 to 1
                float t = (float)i / (numPoints - 1) * 4f * (float)Math.PI;
                float y = (float)Math.Sin(t + phase) * 0.5f; // NDC Y: -0.5 to 0.5
                vertices[i * 2] = x;
                vertices[i * 2 + 1] = y;
            }
            // Update VBO with new sine wave vertices
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            // Clear the screen
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            // Use main shader program
            GL.UseProgram(shaderProgram);
            // Set transform to identity for 2D sine wave and text
            int transformLoc = GL.GetUniformLocation(shaderProgram, "transform");
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(transformLoc, false, ref identity);


            // // Draw sine wave as a line strip
            // GL.BindVertexArray(vao);
            // GL.LineWidth(2.0f);
            // GL.DrawElements(PrimitiveType.LineStrip, numPoints, DrawElementsType.UnsignedInt, 0);
           
           
            // --- Draw simulation time and text using bitmap font ---
            float simTime = (float)phase / (float)speed; // Fake sim time for demo
            string timeStr = $"t = {simTime:F2}s";
            DrawText(timeStr, -0.98f, 0.95f, 12f, 1f, 1f, 0.2f); // Top-left, yellowish
            DrawText("the quick brown fox jumped over the lazy dog", -0.98f, 0.1f, 2f, 1f, 1f, 1f);
            DrawText("THE QUICK BROWN FOX JUMPED\n OVER THE LAZY DOG", -0.98f, -0.1f, 2f, 1f, 1f, 1f);


            // --- Draw 3D wireframe sphere (latitude/longitude lines) ---
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(sphereVao);
            int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
            if (colorLoc >= 0) GL.Uniform3(colorLoc, 0.2f, 0.6f, 1.0f); // Blue color for sphere
            double time = phase / speed;
            // Compose transform: rotate Y (animation), rotate X (tilt), translate X (wobble)

            Matrix4 view = Matrix4.CreateTranslation(new Vector3(0f, 0f, -1f));

            // Matrix4 projection = Matrix4.Identity;
            // Matrix4 view = Matrix4.Identity;
            var model = Matrix4.CreateRotationY(0.1f*(float)(time)) * Matrix4.CreateScale(1f, 1f, 1f);
            var transform = model * view * projection;

            // Set transform to identity for 2D sine wave and text
            transformLoc = GL.GetUniformLocation(shaderProgram, "transform");
            GL.UniformMatrix4(transformLoc, false, ref transform);
            // Draw sphere as wireframe (PolygonMode.Line)

            Console.WriteLine($"Sphere index count: {sphereIndexCount}");
            Console.WriteLine($"Transform matrix: {transform}");
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.DrawElements(PrimitiveType.Triangles, sphereIndexCount, DrawElementsType.UnsignedInt, 0);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill); // Restore fill mode
            GL.BindVertexArray(0);
            // Swap buffers to display the frame
            window.SwapBuffers();
            
        };

        window.UpdateFrame += (FrameEventArgs args) =>
        {
            var input = window.KeyboardState;
            if (input.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape))
            {
                window.Close(); // This exits the window loop
            }
        };

        // --- Resource cleanup: called when window is closed ---
        window.Unload += () =>
        {
            // Delete all OpenGL resources (buffers, shaders, VAOs)
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (shaderProgram != 0) GL.DeleteProgram(shaderProgram);
            if (textVao != 0) GL.DeleteVertexArray(textVao);
            if (textVbo != 0) GL.DeleteBuffer(textVbo);
            if (textShader != 0) GL.DeleteProgram(textShader);
            if (sphereVbo != 0) GL.DeleteBuffer(sphereVbo);
            if (sphereVao != 0) GL.DeleteVertexArray(sphereVao);
            if (sphereEbo != 0) GL.DeleteBuffer(sphereEbo);
        };

        // --- Start the OpenTK event loop (blocks until window closes) ---
        window.Run();
        
        // --- Sine wave and OpenGL state setup ---
        // Set up OpenGL buffers, shaders, and state for the animated sine wave and text rendering
        // This includes VAO/VBO/EBO for the sine wave, and shader compilation for both the sine and text

        // --- Sphere mesh state ---
        // Set up mesh data and OpenGL buffers for the 3D wireframe sphere
        // Sphere vertices and indices are generated for latitude/longitude lines

        // --- Text rendering state ---
        // Set up OpenGL state for text rendering using a bitmap 8x8 font
        // The font data is defined in BitmapFont8x8, and text is drawn using TextRenderer.DrawText

        // --- Main OpenTK event handlers ---
        // window.Load: Initializes all OpenGL resources and mesh data
        // window.RenderFrame: Called every frame to update and render the scene (sine wave, text, sphere)
        // window.Unload: Cleans up all OpenGL resources

        // --- DrawText helper ---
        // Draws ASCII text using the 8x8 bitmap font, as GL_POINTS, at the given NDC position and color.
        // This is a local helper, but could be replaced by TextRenderer.DrawText for modularity.
        void DrawText(string text, float x, float y, float scale, float r, float g, float b)
        {
            // Get font data (8x8 bitmap font, ASCII 32-127)
            var font8x8 = BitmapFont8x8.Font;
            List<float> points = new(); // List of (x, y) points to draw
            float cursorX = x; // Current X position (NDC)
            foreach (char c in text)
            {
                // Skip unknown characters (advance cursor)
                if (!font8x8.TryGetValue(c, out var glyph))
                {
                    cursorX += 9 * scale * 2f / 1200f;
                    continue;
                }
                // For each row in the glyph (8 rows)
                for (int row = 0; row < 8; row++)
                {
                    byte rowData = glyph[row];
                    for (int col = 0; col < 8; col++)
                    {
                        // If bit is set, add a point for this pixel
                        // (col is flipped so bits are drawn left-to-right)
                        if (((rowData >> col) & 1) != 0)
                        {
                            float px = cursorX + col * scale * 2f / 1200f;
                            float py = y - row * scale * 2f / 800f;
                            points.Add(px);
                            points.Add(py);
                        }
                    }
                }
                // Advance cursor for next character (9 units for spacing)
                cursorX += 9 * scale * 2f / 1200f;
            }
            if (points.Count == 0) return; // Nothing to draw
            // Create temporary VBO/VAO for this text draw
            int textVboTmp = GL.GenBuffer();
            int textVaoTmp = GL.GenVertexArray();
            GL.BindVertexArray(textVaoTmp);
            GL.BindBuffer(BufferTarget.ArrayBuffer, textVboTmp);
            GL.BufferData(BufferTarget.ArrayBuffer, points.Count * sizeof(float), points.ToArray(), BufferUsageHint.StreamDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.UseProgram(shaderProgram); // Use the same simple shader as the sine wave
            int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
            if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
            GL.PointSize(scale); // Set point size for font pixels
            GL.DrawArrays(PrimitiveType.Points, 0, points.Count / 2);
            // Clean up temporary resources
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.DeleteBuffer(textVboTmp);
            GL.DeleteVertexArray(textVaoTmp);
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
