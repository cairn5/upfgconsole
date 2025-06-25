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
using lib;
using lib.graphics;

class Handler
{
    // Shared simulation state
    private static Simulator sharedSim;
    private static GuidanceProgram sharedGuidance;
    private static readonly object simLock = new object();
    private static bool simulationRunning = true;
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    static void Main()
    {
        // Initialize simulation
        InitializeSimulation();
        
        // Start simulation on background thread
        Task simulationTask = StartSimulationAsync(cancellationTokenSource.Token);
        
        try
        {
            // Run OpenGL visualizer on main thread (required for OpenGL context)
            Visualizer.PlotRealtimeSync(sharedSim, sharedGuidance);
        }
        finally
        {
            // Clean shutdown
            cancellationTokenSource.Cancel();
            simulationRunning = false;
            
            try
            {
                simulationTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Simulation cancelled successfully.");
            }
        }
    }

    private static void InitializeSimulation()
    {
        string missionPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";
        string simPath = "/home/oli/code/csharp/upfgconsole/upfgconsole/simvars.json";
        
        MissionConfig mission = Utils.ReadMission(missionPath);
        Vehicle veh = Vehicle.FromStagesJson(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;
        
        sharedSim = new Simulator();
        sharedSim.LoadSimVarsFromJson(simPath);
        sharedSim.SetVehicle(veh);
        
        UPFGTarget tgt = new UPFGTarget();
        tgt.Set(desOrbit, sharedSim);
        
        Dictionary<GuidanceMode, IGuidanceTarget> targets = new Dictionary<GuidanceMode, IGuidanceTarget>
        {
            { GuidanceMode.Prelaunch, tgt },
            { GuidanceMode.Ascent, tgt },
            { GuidanceMode.OrbitInsertion, tgt },
            { GuidanceMode.FinalBurn, tgt},
            { GuidanceMode.Idle, tgt}
        };
        
        sharedGuidance = new GuidanceProgram(targets, veh, sharedSim);
    }

    private static async Task StartSimulationAsync(CancellationToken cancellationToken)
    {
        Vehicle veh;
        lock (simLock)
        {
            veh = sharedSim.SimVehicle; // Get a reference to work with
        }

        bool guidanceFailed = false;
        
        // Launch guidance task
        Task guidanceTask = Task.Run(async () =>
        {
            int guidanceIter = 0;
            while (simulationRunning && !cancellationToken.IsCancellationRequested)
            {
                lock (simLock)
                {
                    sharedGuidance.UpdateVehicle(veh);
                    sharedGuidance.Step();
                    sharedSim.SetGuidance(sharedGuidance.GetCurrentSteering(), veh.Stages[0]);
                }
                await Task.Delay((int)(sharedSim.dtguidance * 1000f / sharedSim.simspeed), cancellationToken);
                guidanceIter++;
            }
        }, cancellationToken);

        // Physics loop (fast)
        try
        {
            while (simulationRunning && !cancellationToken.IsCancellationRequested)
            {
                lock (simLock)
                {
                    sharedSim.StepForward();
                    if (sharedSim.State.mass < sharedSim.SimVehicle.CurrentStage.MassDry) //staging logic
                    {
                        if (veh.Stages.Count > 1)
                        {
                            veh.AdvanceStage();
                            sharedSim.SetVehicle(veh);
                        }
                        else
                        {
                            Console.WriteLine("SIMULATION STOPPED - FUEL DEPLETED");
                            break;
                        }
                    }
                }
                await Task.Delay((int)(sharedSim.dt * 1000f / sharedSim.simspeed), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Physics simulation cancelled.");
        }

        try
        {
            await guidanceTask;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Guidance task cancelled.");
        }

        if (!guidanceFailed && !cancellationToken.IsCancellationRequested)
        {
            lock (simLock)
            {
                Utils.PlotTrajectory(sharedSim);
                var kepler = sharedSim.State.Kepler;
                Console.WriteLine(kepler["e"]);
                Utils.PlotOrbit(kepler);
            }
        }
        
        simulationRunning = false;
    }

    // Static methods to safely access simulation data from OpenGL thread
    public static (Vector3 position, Vector3 velocity, double time, float mass) GetCurrentState()
    {
        lock (simLock)
        {
            if (sharedSim?.State != null)
            {
                return (
                    new Vector3(sharedSim.State.r.Y, sharedSim.State.r.Z, sharedSim.State.r.X),
                    new Vector3(sharedSim.State.v.Y, sharedSim.State.v.Z, sharedSim.State.v.X),
                    sharedSim.State.t,
                    sharedSim.State.mass
                );
            }
            return (Vector3.Zero, Vector3.Zero, 0, 0);
        }
    }

    public static List<Vector3> GetTrajectoryHistory()
    {
        lock (simLock)
        {
            if (sharedSim?.History != null)
            {
                var trajectory = new List<Vector3>();
                foreach (var state in sharedSim.History)
                {
                    trajectory.Add(new Vector3(state.r.Y, state.r.Z, state.r.X));
                }
                return trajectory;
            }
            return new List<Vector3>();
        }
    }

    public static (Vector3 steering, GuidanceMode mode) GetGuidanceInfo()
    {
        lock (simLock)
        {
            if (sharedGuidance != null)
            {
                var steering = sharedGuidance.GetCurrentSteering();
                return (
                    new Vector3(steering.Y, steering.Z, steering.X),
                    sharedGuidance.ActiveMode
                );
            }
            return (Vector3.Zero, GuidanceMode.Idle);
        }
    }

    public static bool IsSimulationRunning()
    {
        return simulationRunning;
    }
}