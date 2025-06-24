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
using lib.graphics;


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


