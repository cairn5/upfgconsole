using System;
using System.Collections.Generic;
using ApexCharts;
using Microsoft.JSInterop;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using lib;

namespace dash.Pages
{
    public partial class Charts
    {
        private List<SamplePoint> Points { get; set; } = new();

        private int popOutChart = 0;


        private SimParams simParams = new SimParams();
        private SimResult? result;
        private const string SettingsKey = "simParams";

        [Inject]
        public IJSRuntime JS { get; set; } = default!;
        private string? simParamsJson;

        private async Task RunSimulation()
        {
            // Call your simulation logic here, e.g.:
            // result = Simulator.Run(simParams);
            // For now, just mock a result:

        }

        protected override async Task OnInitializedAsync()
        {
            // Chart 1: y = sin(x) / x
            for (double x = -10; x <= 10; x += 0.03)
            {
                double y = (Math.Abs(x) < 1e-6) ? 1.0 : Math.Sin(x) / x;
                Points.Add(new SamplePoint { X = x, Y = y });
            }
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

    }
}
