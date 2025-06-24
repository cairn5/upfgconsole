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

namespace lib;


public static class Runner
{

    public static async Task RunSim(SimParams simParams, Action<GuidanceProgram> onGuidanceStep, Action<Simulator> onSimStep)
    {
        object simLock = new object();  // Lock to protect shared state


        Vehicle veh = Vehicle.FromStagesDash(simParams.Stages);
        Dictionary<string, float> desOrbit = new Dictionary<string, float>
        {
            {"pe", (float)simParams.Pe },
            {"ap", (float)simParams.Ap },
            {"inc", (float)simParams.Inc }
        };

        Simulator sim = new Simulator();
        sim.LoadSimVarsFromDash(simParams);
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
                var guidanceState = ascentProgram;
                onGuidanceStep?.Invoke(ascentProgram);

                await Task.Delay((int)(simParams.dtguidance * 1000f / sim.simspeed)); // guidance runs slower
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
                
                onSimStep?.Invoke(sim);
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

public class SimResult
{
    public Simulator sim { get; set; }
    public GuidanceProgram guidance { get; set; }
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
