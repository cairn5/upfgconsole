// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ScottPlot.LayoutEngines;
using ConsoleTables;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;

using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

using lib;


class Handler
{
    static void Main()
    {
        // Run the OpenTK visualizer synchronously on the main thread
        Visualizer.PlotRealtimeSync(new Simulator(), new GuidanceProgram(new Dictionary<GuidanceMode, IGuidanceTarget>(), new Vehicle(), new Simulator()));
    }

    static async Task RunAsync()
    {
        object simLock = new object();  // Lock to protect shared state

        string missionPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";
        string simPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/simvars.json";

        MissionConfig mission = Utils.ReadMission(missionPath);
        Vehicle veh = Vehicle.FromStagesJson(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;

        Simulator sim = new Simulator();
        sim.LoadSimVarsFromJson(simPath);
        sim.SetVehicle(veh);

        UPFGTarget tgt = new UPFGTarget();
        tgt.Set(desOrbit, sim);

        Dictionary<GuidanceMode, IGuidanceTarget> targets = new Dictionary<GuidanceMode, IGuidanceTarget>
        {
            { GuidanceMode.Prelaunch, tgt },
            { GuidanceMode.Ascent, tgt },
            { GuidanceMode.OrbitInsertion, tgt },
            { GuidanceMode.FinalBurn, tgt}
        };

        GuidanceProgram ascentProgram = new GuidanceProgram(targets, veh, sim);


        double trem = 2;
        bool guidanceFailed = false;

        // Launch guidance task
        Task guidanceTask = Task.Run(async () =>
        {
            int guidanceIter = 0;

            while (true)
            {
                lock (simLock)
                {
                    ascentProgram.UpdateVehicle(veh);
                    ascentProgram.Step();

                    sim.SetGuidance(ascentProgram.GetCurrentSteering(), veh.Stages[0]);
                    if (ascentProgram.ActiveMode is GuidanceMode.Idle)
                    {
                        Console.WriteLine("Guidance program completed successfully.");
                        break; // Exit the loop if guidance is complete
                    }
                }

                await Task.Delay((int)(0.1 * 1000f / sim.simspeed)); // guidance runs slower
                guidanceIter++;
            }
        });

        // Physics loop (fast)
        while (true)
        {
            lock (simLock)
            {

                if (ascentProgram.ActiveMode is GuidanceMode.Idle)
                {
                    break; // Exit the loop if guidance is complete
                }

                sim.StepForward();

                if (sim.State.mass < sim.SimVehicle.CurrentStage.MassDry) //staging logic
                {
                    if (veh.Stages.Count > 1)
                    {
                        veh.AdvanceStage();
                        sim.SetVehicle(veh);

                    }
                    else
                    {
                        Console.WriteLine("SIMULATION STOPPED - FUEL DEPLETED");

                        break;
                    }
                }
            }

            await Task.Delay((int)(sim.dt * 1000f / sim.simspeed));

        }

        await guidanceTask;

        if (!guidanceFailed)
        {
            Utils.PlotTrajectory(sim);
            var kepler = sim.State.Kepler;
            Console.WriteLine(kepler["e"]);
            Utils.PlotOrbit(kepler);
        }
    }
}

public static class Visualizer
{
    public static void PlotRealtimeSync(Simulator sim, GuidanceProgram guidance)
    {
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1200, 800),
            Title = "OpenTK Modern Shader Sine Wave Demo",
        };

        using var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings);

        int shaderProgram = 0, vao = 0, vbo = 0;
        int numPoints = 400;
        float[] vertices = new float[numPoints * 2];
        uint[] indices = new uint[numPoints];
        double phase = 0;
        double speed = 0.5 * Math.PI / 2; // 1 period every 2 seconds

        // --- Text rendering state ---
        int textVao = 0, textVbo = 0;
        int textShader = 0;

        // --- Bitmap font data (5x7 font, ASCII 32-127, all printable characters) ---
        // Each character is 5x7 pixels, stored as 5 bytes (one per column)
        // Source: https://github.com/dhepper/font8x8/blob/master/font8x8_basic.h (adapted to 5x7)
        Dictionary<char, byte[]> font5x7 = new()
        {
            [' '] = new byte[]{0x00,0x00,0x00,0x00,0x00},
            ['!'] = new byte[]{0x00,0x00,0x5F,0x00,0x00},
            ['"'] = new byte[]{0x00,0x07,0x00,0x07,0x00},
            ['#'] = new byte[]{0x14,0x7F,0x14,0x7F,0x14},
            ['$'] = new byte[]{0x24,0x2A,0x7F,0x2A,0x12},
            ['%'] = new byte[]{0x23,0x13,0x08,0x64,0x62},
            ['&'] = new byte[]{0x36,0x49,0x55,0x22,0x50},
            ['\''] = new byte[]{0x00,0x05,0x03,0x00,0x00},
            ['('] = new byte[]{0x00,0x1C,0x22,0x41,0x00},
            [')'] = new byte[]{0x00,0x41,0x22,0x1C,0x00},
            ['*'] = new byte[]{0x14,0x08,0x3E,0x08,0x14},
            ['+'] = new byte[]{0x08,0x08,0x3E,0x08,0x08},
            [','] = new byte[]{0x00,0x50,0x30,0x00,0x00},
            ['-'] = new byte[]{0x08,0x08,0x08,0x08,0x08},
            ['.'] = new byte[]{0x00,0x60,0x60,0x00,0x00},
            ['/'] = new byte[]{0x20,0x10,0x08,0x04,0x02},
            ['0'] = new byte[]{0x3E,0x51,0x49,0x45,0x3E},
            ['1'] = new byte[]{0x00,0x42,0x7F,0x40,0x00},
            ['2'] = new byte[]{0x42,0x61,0x51,0x49,0x46},
            ['3'] = new byte[]{0x21,0x41,0x45,0x4B,0x31},
            ['4'] = new byte[]{0x18,0x14,0x12,0x7F,0x10},
            ['5'] = new byte[]{0x27,0x45,0x45,0x45,0x39},
            ['6'] = new byte[]{0x3C,0x4A,0x49,0x49,0x30},
            ['7'] = new byte[]{0x01,0x71,0x09,0x05,0x03},
            ['8'] = new byte[]{0x36,0x49,0x49,0x49,0x36},
            ['9'] = new byte[]{0x06,0x49,0x49,0x29,0x1E},
            [':'] = new byte[]{0x00,0x36,0x36,0x00,0x00},
            [';'] = new byte[]{0x00,0x56,0x36,0x00,0x00},
            ['<'] = new byte[]{0x08,0x14,0x22,0x41,0x00},
            ['='] = new byte[]{0x14,0x14,0x14,0x14,0x14},
            ['>'] = new byte[]{0x00,0x41,0x22,0x14,0x08},
            ['?'] = new byte[]{0x02,0x01,0x51,0x09,0x06},
            ['@'] = new byte[]{0x32,0x49,0x79,0x41,0x3E},
            ['A'] = new byte[]{0x7E,0x11,0x11,0x11,0x7E},
            ['B'] = new byte[]{0x7F,0x49,0x49,0x49,0x36},
            ['C'] = new byte[]{0x3E,0x41,0x41,0x41,0x22},
            ['D'] = new byte[]{0x7F,0x41,0x41,0x22,0x1C},
            ['E'] = new byte[]{0x7F,0x49,0x49,0x49,0x41},
            ['F'] = new byte[]{0x7F,0x09,0x09,0x09,0x01},
            ['G'] = new byte[]{0x3E,0x41,0x49,0x49,0x7A},
            ['H'] = new byte[]{0x7F,0x08,0x08,0x08,0x7F},
            ['I'] = new byte[]{0x00,0x41,0x7F,0x41,0x00},
            ['J'] = new byte[]{0x20,0x40,0x41,0x3F,0x01},
            ['K'] = new byte[]{0x7F,0x08,0x14,0x22,0x41},
            ['L'] = new byte[]{0x7F,0x40,0x40,0x40,0x40},
            ['M'] = new byte[]{0x7F,0x02,0x0C,0x02,0x7F},
            ['N'] = new byte[]{0x7F,0x04,0x08,0x10,0x7F},
            ['O'] = new byte[]{0x3E,0x41,0x41,0x41,0x3E},
            ['P'] = new byte[]{0x7F,0x09,0x09,0x09,0x06},
            ['Q'] = new byte[]{0x3E,0x41,0x51,0x21,0x5E},
            ['R'] = new byte[]{0x7F,0x09,0x19,0x29,0x46},
            ['S'] = new byte[]{0x46,0x49,0x49,0x49,0x31},
            ['T'] = new byte[]{0x01,0x01,0x7F,0x01,0x01},
            ['U'] = new byte[]{0x3F,0x40,0x40,0x40,0x3F},
            ['V'] = new byte[]{0x1F,0x20,0x40,0x20,0x1F},
            ['W'] = new byte[]{0x3F,0x40,0x38,0x40,0x3F},
            ['X'] = new byte[]{0x63,0x14,0x08,0x14,0x63},
            ['Y'] = new byte[]{0x07,0x08,0x70,0x08,0x07},
            ['Z'] = new byte[]{0x61,0x51,0x49,0x45,0x43},
            ['['] = new byte[]{0x00,0x7F,0x41,0x41,0x00},
            ['\\'] = new byte[]{0x02,0x04,0x08,0x10,0x20},
            [']'] = new byte[]{0x00,0x41,0x41,0x7F,0x00},
            ['^'] = new byte[]{0x04,0x02,0x01,0x02,0x04},
            ['_'] = new byte[]{0x40,0x40,0x40,0x40,0x40},
            ['`'] = new byte[]{0x00,0x01,0x02,0x04,0x00},
            ['a'] = new byte[]{0x20,0x54,0x54,0x54,0x78},
            ['b'] = new byte[]{0x7F,0x48,0x44,0x44,0x38},
            ['c'] = new byte[]{0x38,0x44,0x44,0x44,0x20},
            ['d'] = new byte[]{0x38,0x44,0x44,0x48,0x7F},
            ['e'] = new byte[]{0x38,0x54,0x54,0x54,0x18},
            ['f'] = new byte[]{0x08,0x7E,0x09,0x01,0x02},
            ['g'] = new byte[]{0x0C,0x52,0x52,0x52,0x3E},
            ['h'] = new byte[]{0x7F,0x08,0x04,0x04,0x78},
            ['i'] = new byte[]{0x00,0x44,0x7D,0x40,0x00},
            ['j'] = new byte[]{0x20,0x40,0x44,0x3D,0x00},
            ['k'] = new byte[]{0x7F,0x10,0x28,0x44,0x00},
            ['l'] = new byte[]{0x00,0x41,0x7F,0x40,0x00},
            ['m'] = new byte[]{0x7C,0x04,0x18,0x04,0x78},
            ['n'] = new byte[]{0x7C,0x08,0x04,0x04,0x78},
            ['o'] = new byte[]{0x38,0x44,0x44,0x44,0x38},
            ['p'] = new byte[]{0x7C,0x14,0x14,0x14,0x08},
            ['q'] = new byte[]{0x08,0x14,0x14,0x18,0x7C},
            ['r'] = new byte[]{0x7C,0x08,0x04,0x04,0x08},
            ['s'] = new byte[]{0x48,0x54,0x54,0x54,0x20},
            ['t'] = new byte[]{0x04,0x3F,0x44,0x40,0x20},
            ['u'] = new byte[]{0x3C,0x40,0x40,0x20,0x7C},
            ['v'] = new byte[]{0x1C,0x20,0x40,0x20,0x1C},
            ['w'] = new byte[]{0x3C,0x40,0x30,0x40,0x3C},
            ['x'] = new byte[]{0x44,0x28,0x10,0x28,0x44},
            ['y'] = new byte[]{0x0C,0x50,0x50,0x50,0x3C},
            ['z'] = new byte[]{0x44,0x64,0x54,0x4C,0x44},
            ['{'] = new byte[]{0x00,0x08,0x36,0x41,0x00},
            ['|'] = new byte[]{0x00,0x00,0x7F,0x00,0x00},
            ['}'] = new byte[]{0x00,0x41,0x36,0x08,0x00},
            ['~'] = new byte[]{0x08,0x04,0x08,0x10,0x08},
        };

        // Simple shader for textured quad
        string textVertexShaderSrc = "#version 330 core\nlayout(location=0) in vec2 aPos;layout(location=1) in vec2 aTex;out vec2 TexCoord;void main(){gl_Position=vec4(aPos,0,1);TexCoord=aTex;}";
        string textFragmentShaderSrc = "#version 330 core\nin vec2 TexCoord;out vec4 FragColor;uniform sampler2D tex;void main(){FragColor=texture(tex,TexCoord);}";

        window.Load += () =>
        {
            Console.WriteLine($"OpenGL Version: {GL.GetString(StringName.Version)}");
            string vertexShaderSource = "#version 330 core\nlayout(location = 0) in vec2 aPosition;void main(){gl_Position = vec4(aPosition, 0.0, 1.0);}";
            string fragmentShaderSource = "#version 330 core\nout vec4 FragColor;void main(){FragColor = vec4(0.2, 1.0, 0.2, 1.0);}";
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            string vLog = GL.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(vLog)) Console.WriteLine($"Vertex shader log: {vLog}");
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);
            string fLog = GL.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(fLog)) Console.WriteLine($"Fragment shader log: {fLog}");
            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);
            string pLog = GL.GetProgramInfoLog(shaderProgram);
            if (!string.IsNullOrWhiteSpace(pLog)) Console.WriteLine($"Program link log: {pLog}");
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            // Setup indices for line strip
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
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1f);

            // Text shader
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
            // Text quad VAO/VBO (NDC: top-left at (-1,1), size 0.4x0.1)
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
        };

        // Helper: draw text at (x, y) in NDC [-1,1], with scale (pixel size in NDC)
        // Modern OpenGL version: builds a VBO of points and draws with a shader
        void DrawText(string text, float x, float y, float scale, float r, float g, float b)
        {
            List<float> points = new();
            float cursorX = x;
            foreach (char c in text)
            {
                if (!font5x7.TryGetValue(c, out var glyph))
                {
                    cursorX += 6 * scale * 2f / 1200f;
                    continue;
                }
                for (int col = 0; col < 5; col++)
                {
                    byte colData = glyph[col];
                    for (int row = 0; row < 7; row++)
                    {
                        if (((colData >> row) & 1) != 0)
                        {
                            float px = cursorX + col * scale * 2f / 1200f;
                            float py = y - row * scale * 2f / 800f;
                            points.Add(px);
                            points.Add(py);
                        }
                    }
                }
                cursorX += 6 * scale * 2f / 1200f;
            }
            if (points.Count == 0) return;
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
            GL.PointSize(scale);
            GL.DrawArrays(PrimitiveType.Points, 0, points.Count / 2);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.DeleteBuffer(textVboTmp);
            GL.DeleteVertexArray(textVaoTmp);
        }

        window.RenderFrame += (FrameEventArgs args) =>
        {
            // Update sine wave vertices
            phase += speed * args.Time;
            for (int i = 0; i < numPoints; i++)
            {
                float x = (float)i / (numPoints - 1) * 2f - 1f; // NDC X: -1 to 1
                float t = (float)i / (numPoints - 1) * 4f * (float)Math.PI;
                float y = (float)Math.Sin(t + phase) * 0.5f; // NDC Y: -0.5 to 0.5
                vertices[i * 2] = x;
                vertices[i * 2 + 1] = y;
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.LineWidth(2.0f);
            GL.DrawElements(PrimitiveType.LineStrip, numPoints, DrawElementsType.UnsignedInt, 0);
            // --- Draw sim time as text ---
            float simTime = (float)phase / (float)speed; // fake sim time for demo
            string timeStr = $"t = {simTime:F2}s";
            DrawText(timeStr, -0.98f, 0.95f, 12f, 1f, 1f, 0.2f); // top-left, yellowish
            DrawText("the quick brown fox jumped over the lazy dog", -0.98f, 0.1f, 2f, 1f, 1f, 1f);
            DrawText("THE QUICK BROWN FOX JUMPED\n OVER THE LAZY DOG", -0.98f, -0.1f, 2f, 1f, 1f, 1f);
            window.SwapBuffers();
        };

        window.Unload += () =>
        {
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (shaderProgram != 0) GL.DeleteProgram(shaderProgram);
            if (textVao != 0) GL.DeleteVertexArray(textVao);
            if (textVbo != 0) GL.DeleteBuffer(textVbo);
            if (textShader != 0) GL.DeleteProgram(textShader);
        };

        window.Run();
    }
}

