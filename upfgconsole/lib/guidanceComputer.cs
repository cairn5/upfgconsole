namespace lib;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Security;
using ScottPlot.LayoutEngines;
using ConsoleTables;
using System.Xml;

public enum GuidanceMode
{
    Idle,
    Prelaunch,
    Ascent,
    OrbitInsertion,
    FinalBurn,
    Abort,
}

public interface IGuidanceTarget { }
// Use the existing Target (UPFGTarget) class for UPFG
// Example for another target type
public class GravityTurnTarget : IGuidanceTarget
{
    // Add properties specific to gravity turn targeting if needed
}

public interface IGuidanceMode
{
    bool Converged { get; }
    bool StagingFlag { get; set; }
    // SimState? GuidanceHistory { get; set; }
    GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh);
    Vector3? GetSteering();
}

public class UpfgMode : IGuidanceMode
{
    public Upfg upfg = new Upfg();
    public bool Converged => upfg.ConvergenceFlag;
    public bool StagingFlag { get; set; } = false;
    public Vector3? PrevSteering = new();

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh)
    {
        if (tgt is UPFGTarget upfgTarget)
        {
            PrevSteering = sim.ThrustVector;
            if (StagingFlag)
            {
                upfg.StageEvent();
                StagingFlag = false;
            }
            upfg.step(sim, veh, upfgTarget);

            if (upfg.PrevVals.tgo < 5) return GuidanceMode.FinalBurn;
            else return null;
        }
        else return null;
    }
    public Vector3? GetSteering()
    {
        return upfg.Steering;
    }

    public string userOutput(Simulator sim)
    {
        
        // Transposed table: each parameter is a row
        var upfgTable = new ConsoleTable("PARAM", "VALUE");
        upfgTable.AddRow("TB", upfg.PrevVals.tb.ToString("F1").PadLeft(6));
        upfgTable.AddRow("TGO", upfg.PrevVals.tgo.ToString("F1").PadLeft(6));
        upfgTable.AddRow("VGO", upfg.PrevVals.vgo.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGO", (upfg.PrevVals.rd - sim.State.r).Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGRAV", upfg.PrevVals.rgrav.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RBIAS", upfg.PrevVals.rbias.Length().ToString("F1").PadLeft(6));
        return upfgTable.ToString();
    
    }
}

public class FinalMode : IGuidanceMode
{
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;
    public float BurnTime { get; set; } = 5f;
    public float InitialTime { get; set; } = -1f;
    public Vector3 _PrevGuidance = Vector3.Zero;

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh)
    {
        _PrevGuidance = sim.ThrustVector;

        
        if (InitialTime == -1)
        {
            InitialTime = sim.State.t;
        }

        if (sim.State.t > InitialTime + BurnTime)
        {
            return GuidanceMode.Idle;
        }
        else return null;

    }
    public Vector3? GetSteering() => null; // Assuming sim.ThrustVector is the final steering vector
}

public class IdleMode : IGuidanceMode
{
    public bool Converged => true; // Idle mode is always converged
    public bool StagingFlag { get; set; } = false;
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh)
    {
        return null;
    }
    public Vector3? GetSteering() => Vector3.Zero; // No steering in idle mode
}

public class PreLaunchMode : IGuidanceMode
{
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh)
    {
        System.Threading.Thread.Sleep(100);
        return GuidanceMode.Ascent;
    }
    public Vector3? GetSteering() => Vector3.Zero;
}

public class GravityTurnMode : IGuidanceMode
{
    private GravityTurn gravityTurn = new GravityTurn();
    public bool Converged => false; // TODO: Implement convergence logic for GravityTurn
    public bool StagingFlag { get; set; } = false;
    public void Setup(Simulator sim, IGuidanceTarget tgt)
    {
        // If GravityTurn ever needs a target, handle it here
        // For now, nothing to do
    }
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget tgt, Vehicle veh)
    {
        if (tgt is UPFGTarget upfgTarget)
            gravityTurn.step(sim, upfgTarget);
        else
            throw new ArgumentException("GravityTurnMode requires a UPFGTarget as its target.");
        // TODO: Add convergence logic and return next mode if needed
        if (sim.State.Misc["altitude"] > 30e3)
        {
            return GuidanceMode.OrbitInsertion; //advance to next mode if altitude is above 50km
        }
        return null;
    }
    public Vector3? GetSteering() => gravityTurn.guidance;
}



public class GuidanceProgram
{
    public Dictionary<GuidanceMode, IGuidanceMode> Modes { get; set; } = new();
    public Dictionary<GuidanceMode, IGuidanceTarget> Targets { get; set; } = new();
    public GuidanceMode ActiveMode { get; set; }
    public Vehicle Vehicle { get; set; }
    public Simulator Simulator { get; set; }
    public Vector3? steering { get; set; }
    public bool StagingFlag { get; set; }
    protected int _lastStageCount;
    public float dt { get; set; } = 1.0f;

    public GuidanceProgram(Dictionary<GuidanceMode, IGuidanceTarget> targets, Vehicle veh, Simulator sim, Mission mission)
    {
        Vehicle = veh;
        _lastStageCount = veh.Stages.Count();
        Simulator = sim;
        Modes[GuidanceMode.Prelaunch] = new PreLaunchMode();
        Modes[GuidanceMode.Ascent] = new GravityTurnMode();
        Modes[GuidanceMode.OrbitInsertion] = new UpfgMode();
        Modes[GuidanceMode.FinalBurn] = new FinalMode();
        Modes[GuidanceMode.Idle] = new IdleMode();
        Targets = targets;
        ActiveMode = GuidanceMode.Prelaunch;
    }

    public void Step()
    {
        var mode = Modes[ActiveMode];
        var tgt = Targets.ContainsKey(ActiveMode) ? Targets[ActiveMode] : null;
        // Propagate staging flag to the mode
        mode.StagingFlag = this.StagingFlag;
        var nextMode = mode.Step(Simulator, tgt, Vehicle);
        // Reset after use
        this.StagingFlag = false;

        Vector3? newsteering = mode.GetSteering();
        if (newsteering != null)
        {
            steering = newsteering;
        }

        if (nextMode.HasValue && Modes.ContainsKey(nextMode.Value))
            {
                ActiveMode = nextMode.Value;
            }
    }

    public void UpdateVehicle(Vehicle veh)
    {
        if (_lastStageCount != veh.Stages.Count)
        {
            StagingFlag = true;
        }
        _lastStageCount = veh.Stages.Count;
        Vehicle = veh;
    }

    public Vector3 GetCurrentSteering()
    {
        return steering ?? Vector3.Zero;
    }
}

public class AscentProgram : GuidanceProgram
{
    public AscentProgram(Dictionary<GuidanceMode, IGuidanceTarget> targets, Vehicle veh, Simulator sim, Mission mission)
        : base(targets, veh, sim, mission)
    {
        dt = mission.Guidance.dt;
        Vehicle = veh;
        _lastStageCount = veh.Stages.Count();
        Simulator = sim;
        Modes[GuidanceMode.Prelaunch] = new PreLaunchMode();
        Modes[GuidanceMode.Ascent] = new GravityTurnMode();
        Modes[GuidanceMode.OrbitInsertion] = new UpfgMode();
        Modes[GuidanceMode.FinalBurn] = new FinalMode();
        Modes[GuidanceMode.Idle] = new IdleMode();
        Targets = targets;
        ActiveMode = GuidanceMode.Prelaunch;
    }
}

