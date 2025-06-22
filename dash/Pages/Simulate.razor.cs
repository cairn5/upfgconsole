using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using lib;

namespace dash.Pages
{
    public partial class Simulate : ComponentBase
    {
        private SimParams simParams = new SimParams();
        private SimResult? result;
        private const string SettingsKey = "simParams";

        [Inject] private IJSRuntime JS { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            var json = await JS.InvokeAsync<string>("simSettings.load", SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    simParams = JsonSerializer.Deserialize<SimParams>(json) ?? new SimParams();
                }
                catch { simParams = new SimParams(); }
            }
        }

        private async Task SaveSettingsAsync()
        {
            var json = JsonSerializer.Serialize(simParams);
            await JS.InvokeVoidAsync("simSettings.save", SettingsKey, json);
        }

        private async Task OnFileSelected(InputFileChangeEventArgs e)
        {
            var file = e.File;
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Orbit", out var orbit))
            {
                simParams.Pe = orbit.GetProperty("pe").GetDouble();
                simParams.Ap = orbit.GetProperty("ap").GetDouble();
                simParams.Inc = orbit.GetProperty("inc").GetDouble();
            }
            if (root.TryGetProperty("StageList", out var stages))
            {
                simParams.StageCount = stages.GetArrayLength();
                simParams.Stages.Clear();
                foreach (var stage in stages.EnumerateArray())
                {
                    simParams.Stages.Add(new Stage
                    {
                        Id = stage.GetProperty("Id").GetInt32(),
                        Mode = stage.GetProperty("Mode").GetInt32(),
                        GLim = stage.GetProperty("GLim").GetDouble(),
                        MassTotal = stage.GetProperty("MassTotal").GetDouble(),
                        MassDry = stage.GetProperty("MassDry").GetDouble(),
                        Thrust = stage.GetProperty("Thrust").GetDouble(),
                        Isp = stage.GetProperty("Isp").GetDouble()
                    });
                }
            }
            await SaveSettingsAsync();
            StateHasChanged();
        }

        private async Task AddStage()
        {
            simParams.Stages.Add(new Stage());
            simParams.StageCount = simParams.Stages.Count;
            await SaveSettingsAsync();
        }

        private async Task RemoveStage(Stage stage)
        {
            simParams.Stages.Remove(stage);
            simParams.StageCount = simParams.Stages.Count;
            await SaveSettingsAsync();
        }

        private async Task SaveToJson()
        {
            var saveObj = new
            {
                Orbit = new { mode = 1, pe = simParams.Pe, ap = simParams.Ap, inc = simParams.Inc },
                StageList = simParams.Stages
            };
            var json = JsonSerializer.Serialize(saveObj, new JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var fileName = $"vehicle_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            using var stream = new MemoryStream(bytes);
            using var streamRef = new DotNetStreamReference(stream);
            await JS.InvokeVoidAsync("downloadFileFromStream", fileName, streamRef);
            await SaveSettingsAsync();
        }

        private async Task RunSimulation()
        {
            // Call your simulation logic here, e.g.:
            // result = Simulator.Run(simParams);
            // For now, just mock a result:
            result = new SimResult();
            result.FinalMass = 2;
            result.FinalVelocity = 1;
            await SaveSettingsAsync();
        }


    }

    public class SimParams
        {
            public double TargetRadius { get; set; }
            public double Pe { get; set; }
            public double Ap { get; set; }
            public double Inc { get; set; }
            public int StageCount { get; set; }
            public List<Stage> Stages { get; set; } = new();
            public double Speed { get; set; }
            public double dtsim { get; set; }
            public double dtguidance { get; set; }
            public double StartLat { get; set; }
            public double StartLong { get; set; }
            public double StartGround { get; set; }
            public double AirVel { get; set; }
            public double AirFpa { get; set; }
            public double Altitude { get; set; }
            public double Mode { get; set; }
        }

    public class SimResult
        {
            public double FinalMass { get; set; }
            public double FinalVelocity { get; set; }
        }
}
