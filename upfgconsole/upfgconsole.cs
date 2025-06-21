// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ScottPlot.LayoutEngines;
using lib;
using ConsoleTables;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

class Handler
{
    static async Task Main()
    {
        await RunAsync();
    }

    static async Task RunAsync()
    {
        object simLock = new object();  // Lock to protect shared state

        string missionPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";
        string simPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/simvars.json";

        MissionConfig mission = Utils.ReadMission(missionPath);
        Vehicle veh = Vehicle.FromStages(mission);
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
            { GuidanceMode.OrbitInsertion, tgt }
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
                    Console.WriteLine(ascentProgram.GetCurrentSteering().X);
                    Console.WriteLine(ascentProgram.GetCurrentSteering().Y);
                    Console.WriteLine(ascentProgram.GetCurrentSteering().Z);
                    Console.WriteLine(ascentProgram.ActiveMode.ToString());
                    if (ascentProgram.ActiveMode is GuidanceMode.Idle)
                    {
                        Console.WriteLine("Guidance program completed successfully.");
                        break; // Exit the loop if guidance is complete
                    }
                }

                await Task.Delay((int)(0.5*100f)); // guidance runs slower
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
                    Console.WriteLine("Guidance program completed successfully.");
                    break; // Exit the loop if guidance is complete
                }
                
                sim.StepForward();
                // Utils.PrintVars(sim, tgt, veh);

                if (sim.State.mass < sim.SimVehicle.CurrentStage.MassDry)
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

            await Task.Delay((int)(sim.dt * 100f));
    
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

