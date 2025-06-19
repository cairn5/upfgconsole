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

        float lat = 28.5f;
        string missionPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";
        string simPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/simvars.json";

        MissionConfig mission = Utils.ReadMission(missionPath);
        Vehicle veh = Vehicle.FromStages(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;

        Simulator sim = new Simulator();
        sim.LoadSimVarsFromJson(simPath);
        sim.SetVehicle(veh);

        UPFGTarget tgt = new UPFGTarget();
        tgt.SetTarget(desOrbit, sim);

        Upfg guidance = new Upfg();
        guidance.Setup(sim, tgt);

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
                    guidance.Run(sim, tgt, veh);
                    

                    if (guidance.PrevVals.tgo < trem || guidanceIter > 1000)
                    {
                        guidanceFailed = true;
                        Console.WriteLine("GUIDANCE FAIL");
                        break;
                    }

                    if (guidance.ConvergenceFlag)
                    {
                        sim.SetGuidance(guidance.Steering, veh.Stages[0]);
                    }
                }

                await Task.Delay(1000); // guidance runs slower
                guidanceIter++;
            }
        });

        // Physics loop (fast)
        while (!guidanceFailed && guidance.PrevVals.tgo > trem)
        {
            lock (simLock)
            {
                
                sim.StepForward();
                Utils.PrintVars(guidance, sim, tgt, veh);

                if (sim.State.mass < sim.SimVehicle.CurrentStage.MassDry)
                {
                    if (veh.Stages.Count > 1)
                    {
                        veh.AdvanceStage();
                        sim.SetVehicle(veh);

                    }
                    else
                    {
                        Console.WriteLine("UPFG FAILED - INSUFFICIENT DV");
                        guidanceFailed = true;
                        break;
                    }
                }
            }

            await Task.Delay((int)(sim.dt * 1000f));
    
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

