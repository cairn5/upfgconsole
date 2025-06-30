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

            var resultJson = await JS.InvokeAsync<string>("simSettings.load", "simResult");
            if (!string.IsNullOrEmpty(resultJson))
            {
                try
                {
                    result = JsonSerializer.Deserialize<SimResult>(resultJson);
                }
                catch { result = null; }
            }
        }

        private async Task SaveSettingsAsync()
        {
            var json = JsonSerializer.Serialize(simParams);
            await JS.InvokeVoidAsync("simSettings.save", SettingsKey, json);
            if (result != null)
            {
                var resultJson = JsonSerializer.Serialize(result);
                await JS.InvokeVoidAsync("simSettings.save", "simResult", resultJson);
            }
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

        protected async Task RunSimulation()
        {
            // Optionally clear previous result
            result = null;

            await Runner.RunSim(
                simParams,
                onGuidanceStep: (guidance) =>
                {
                    // This runs at each guidance step
                    // You can collect guidance data, update UI, etc.
                },
                onSimStep: (sim) =>
                {
                    // This runs at each sim step
                    // You can collect sim data, update UI, etc.
                }
            );

            // Optionally, set result after simulation
            result = new SimResult();
            await SaveSettingsAsync();
        }

        private async Task OnTableValueChanged(ChangeEventArgs e)
        {
            await SaveSettingsAsync();
        }

    }
}




