using System;
using System.Collections.Generic;
using ApexCharts;
using Microsoft.JSInterop;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using lib;
using Blazor.Extensions.Canvas;
using Blazor.Extensions.Canvas.Canvas2D;
using Blazor.Extensions;

namespace dash.Pages
{
    public partial class Charts
    {

        private List<SamplePoint> drp;

        private ApexChart<SamplePoint> chart;
        private List<SamplePoint> Points { get; set; } = new();
        private int popOutChart = 0;


        private SimParams simParams = new SimParams();
        private Simulator? simcopy;
        private GuidanceProgram? guidancecopy;
        private SimResult? result;
        private const string SettingsKey = "simParams";

        [Inject]
        public IJSRuntime JS { get; set; } = default!;
        private string? simParamsJson;

        private Canvas2DContext _context;

        protected BECanvasComponent _canvasReference;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            this._context = await this._canvasReference.CreateCanvas2DAsync();
            if (firstRender)
            {
                // _ = AnimateSineWave(); // Start animation, fire and forget
            }
        }

        private async Task AnimateSineWave()
        {
            double width = 1200;
            double height = 800;
            double scaleX = width / (2 * Math.PI); // fit one period
            double scaleY = height / 2 * 0.5; // 90% of half height
            double xOffset = 0;
            double yOffset = height / 2;
            double phase = 0;
            int step = 20; // Increase step size for fewer points per frame
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            try
            {
                while (true)
                {
                    long frameStart = sw.ElapsedMilliseconds;
                    await _context.SetFillStyleAsync("black");
                    await _context.FillRectAsync(0, 0, width, height);
                    await _context.SetStrokeStyleAsync("green");
                    await _context.SetLineWidthAsync(2);
                    await _context.BeginPathAsync();
                    bool first = true;
                    List<double> pxlist = new();
                    List<double> pylist = new();
                    for (double x = 0; x <= width; x += step)
                    {
                        double xVal = (x / scaleX) * 2 * Math.PI / width * width;
                        double y = Math.Sin(xVal + phase);
                        double px = xOffset + x;
                        double py = yOffset - y * scaleY;
                        if (first)
                        {
                            await _context.MoveToAsync(px, py);
                            first = false;
                        }
                        else
                        {
                            await _context.LineToAsync(px, py);
                        }
                    }
                    await _context.StrokeAsync();
                    await this._context.SetFontAsync("48px sans-serif");
                    await this._context.SetFillStyleAsync("green");
                    await this._context.FillTextAsync("Hello World", 500, 500);
                    // Calculate elapsed time and adjust phase increment for smooth, time-based animation
                    long frameEnd = sw.ElapsedMilliseconds;
                    double elapsedSeconds = (frameEnd - frameStart) / 1000.0;
                    phase += 0.05 * 30 * elapsedSeconds; // 60 is target FPS
                    // Always yield to UI thread to avoid freezing
                    await Task.Delay(33);
                }
            }
            catch
            {
                // Optionally log or handle animation errors
            }
        }

        private async Task plotSimulator(Simulator sim)
        {
            double xscale = 1 / 1e4;
            double yscale = 1 / 1e3;

            double width = 1200;
            double height = 800;
            double xOffset = 0;
            double yOffset = height / 2;
            bool first = true;

            // Clear canvas and set up stroke only once
            await _context.SetFillStyleAsync("black");
            await _context.FillRectAsync(0, 0, width, height);
            await _context.SetStrokeStyleAsync("green");
            await _context.SetLineWidthAsync(3);
            await _context.BeginPathAsync();

            for (int i = 0; i < sim.History.Count; i++)
            {
                var pt = sim.History[i];
                double drpsingle = lib.Utils.CalcDownRange(pt, sim.History[0]);
                double altitude = sim.History[i].Misc["altitude"];
                double x = 100 + drpsingle * xscale;
                double y = height - (altitude * yscale); // invert y for canvas

                if (first)
                {
                    await _context.MoveToAsync(x, y);
                    first = false;
                }
                else
                {
                    await _context.LineToAsync(x, y);
                }
            }
            await _context.StrokeAsync();
            Console.WriteLine(sim.State.r.X);
            
            
            
        }

        public async Task RunSimulation()
        {
            // Reload simParams from local storage to ensure latest settings
            simParamsJson = await JS.InvokeAsync<string>("simSettings.load", SettingsKey);
            if (!string.IsNullOrEmpty(simParamsJson))
            {
                try
                {
                    Console.WriteLine($"simParamsJson: {simParamsJson}"); // Debug log
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    simParams = JsonSerializer.Deserialize<SimParams>(simParamsJson, options) ?? new SimParams();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Deserialization error: {ex.Message}");
                    simParams = new SimParams();
                }
            }

            // Optionally clear previous result
            result = null;
            StopPlotLoop(); // Stop any previous plot loop
            _ = StartPlotLoop(); // Start background plot loop (fire-and-forget)

            await Runner.RunSim(
                simParams,
                onGuidanceStep: (guidance) =>
                {
                    guidancecopy = guidance;
                    // This runs at each guidance step
                    // You can collect guidance data, update UI, etc.
                    // Example: Console.WriteLine($"Guidance mode: {guidance.ActiveMode}");
                },
                onSimStep: (sim) =>
                {
                    simcopy = sim;
                    // This runs at each sim step
                    // You can collect sim data, update UI, etc.
                    // Example: Console.WriteLine($"Sim time: {sim.State.time}");
                }
            );

            StopPlotLoop(); // Stop plot loop when simulation ends
            // // Optionally, set result after simulation
            // result = new SimResult
            // {

            // };

        }

        protected override async Task OnInitializedAsync()
        {

            simParamsJson = await JS.InvokeAsync<string>("simSettings.load", SettingsKey);
            if (!string.IsNullOrEmpty(simParamsJson))
            {
                try
                {
                    simParams = JsonSerializer.Deserialize<SimParams>(simParamsJson) ?? new SimParams();
                }
                catch { simParams = new SimParams(); }
            }

        }

        private void PopOutChart(int chartNum)
        {
            popOutChart = chartNum;
        }
        private void ClosePopOut()
        {
            popOutChart = 0;
        }

        private ApexChartOptions<SamplePoint> GetChartOptions()
        {
            return new ApexChartOptions<SamplePoint>
            {
                Chart = new Chart
                {
                    Toolbar = new Toolbar
                    {
                        Show = false
                    },
                    DropShadow = new DropShadow
                    {
                        Enabled = true,
                        Color = "",
                        Top = 18,
                        Left = 7,
                        Blur = 10,
                        Opacity = 0.2d
                    }
                },
                Yaxis = new List<YAxis>
                {
                    new YAxis
                    {
                        Show = false
                    }
                },
                DataLabels = new ApexCharts.DataLabels
                {
                    OffsetY = -6d
                }
            };
        }

        public class SamplePoint
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        public class SimResult
        {
            public double[]? X { get; set; }
            public double[]? Y { get; set; }
        }

        private CancellationTokenSource? plotCts;

        private async Task StartPlotLoop()
        {
            plotCts = new CancellationTokenSource();
            var token = plotCts.Token;
            while (!token.IsCancellationRequested)
            {
                if (simcopy != null)
                {
                    await plotSimulator(simcopy);
                }
                await Task.Delay(500, token); // update plot every 100ms
            }
        }

        private void StopPlotLoop()
        {
            plotCts?.Cancel();
        }
    }
}
