namespace lib;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Linq;
using ConsoleTables;

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
    void Initialize(GuidanceModeConfig config);
    GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null);
    Vector3? GetSteering();
}

public class UpfgMode : IGuidanceMode
{
    private readonly Upfg _upfg = new Upfg();
    private UpfgModeConfig _config = new UpfgModeConfig();
    
    public bool Converged => _upfg.ConvergenceFlag;
    public bool StagingFlag { get; set; } = false;
    public Vector3? PrevSteering { get; private set; } = new();

    public void Initialize(GuidanceModeConfig config)
    {
        if (config is UpfgModeConfig upfgConfig)
        {
            _config = upfgConfig;
        }
    }

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null)
    {
        if (tgt is UPFGTarget upfgTarget)
        {
            PrevSteering = sim.ThrustVector;
            if (StagingFlag)
            {
                _upfg.StageEvent();
                StagingFlag = false;
            }
            _upfg.step(sim, veh, upfgTarget, _config.ModeKey);

            if (_upfg.PrevVals.tgo < _config.ConvergenceThreshold) 
                return GuidanceMode.FinalBurn;
            return null;
        }
        return null;
    }
    
    public Vector3? GetSteering()
    {
        return _upfg.Steering;
    }

    public string userOutput(Simulator sim)
    {
        // Transposed table: each parameter is a row
        var upfgTable = new ConsoleTable("PARAM", "VALUE");
        upfgTable.AddRow("TB", _upfg.PrevVals.tb.ToString("F1").PadLeft(6));
        upfgTable.AddRow("TGO", _upfg.PrevVals.tgo.ToString("F1").PadLeft(6));
        upfgTable.AddRow("VGO", _upfg.PrevVals.vgo.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGO", (_upfg.PrevVals.rd - sim.State.r).Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RGRAV", _upfg.PrevVals.rgrav.Length().ToString("F1").PadLeft(6));
        upfgTable.AddRow("RBIAS", _upfg.PrevVals.rbias.Length().ToString("F1").PadLeft(6));
        return upfgTable.ToString();
    }
}

public class FinalMode : IGuidanceMode
{
    private FinalModeConfig _config = new FinalModeConfig();
    private float _initialTime = -1f;
    private Vector3 _prevGuidance = Vector3.Zero;
    
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;

    public void Initialize(GuidanceModeConfig config)
    {
        if (config is FinalModeConfig finalConfig)
        {
            _config = finalConfig;
        }
    }

    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null)
    {
        _prevGuidance = sim.ThrustVector;

        if (_initialTime == -1)
        {
            _initialTime = sim.State.t;
        }

        if (sim.State.t > _initialTime + _config.BurnTime)
        {
            return GuidanceMode.Idle;
        }
        return null;
    }
    
    public Vector3? GetSteering() => null; // Assuming sim.ThrustVector is the final steering vector
}

public class IdleMode : IGuidanceMode
{
    public bool Converged => true; // Idle mode is always converged
    public bool StagingFlag { get; set; } = false;
    
    public void Initialize(GuidanceModeConfig config) { }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null)
    {
        return null;
    }
    public Vector3? GetSteering() => Vector3.Zero; // No steering in idle mode
}

public class PreLaunchMode : IGuidanceMode
{
    private PreLaunchModeConfig _config = new PreLaunchModeConfig();
    private float? _startTime;
    
    public bool Converged => true;
    public bool StagingFlag { get; set; } = false;

    public void Initialize(GuidanceModeConfig config)
    {
        if (config is PreLaunchModeConfig prelaunchConfig)
        {
            _config = prelaunchConfig;
        }
    }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null)
    {
        _startTime ??= sim.State.t;
        
        if (sim.State.t - _startTime >= _config.Duration)
        {
            return GuidanceMode.Ascent;
        }
        return null; // Stay in prelaunch mode
    }
    
    public Vector3? GetSteering() => Vector3.Zero;
}

public class GravityTurnMode : IGuidanceMode
{
    private readonly GravityTurn _gravityTurn = new GravityTurn();
    private GravityTurnModeConfig _config = new GravityTurnModeConfig();
    
    public bool Converged => false; // TODO: Implement convergence logic for GravityTurn
    public bool StagingFlag { get; set; } = false;

    public void Initialize(GuidanceModeConfig config)
    {
        if (config is GravityTurnModeConfig gravityConfig)
        {
            _config = gravityConfig;
        }
    }
    
    public void Setup(Simulator sim, IGuidanceTarget tgt)
    {
        // If GravityTurn ever needs a target, handle it here
        // For now, nothing to do
    }
    
    public GuidanceMode? Step(Simulator sim, IGuidanceTarget? tgt, Vehicle veh, GuidanceProgram? program = null)
    {
        if (tgt is UPFGTarget upfgTarget)
            _gravityTurn.step(sim, upfgTarget);
        else
            throw new ArgumentException("GravityTurnMode requires a UPFGTarget as its target.");
            
        // Check if altitude threshold is reached
        if (sim.State.Misc.TryGetValue("altitude", out var altitude) && altitude > _config.AltitudeThreshold)
        {
            // Try to get next mode from sequence, fallback to OrbitInsertion if no sequence
            return program?.GetNextModeInSequence(GuidanceMode.Ascent) ?? GuidanceMode.OrbitInsertion;
        }
        return null;
    }
    
    public Vector3? GetSteering() => _gravityTurn.guidance;
}



public class GuidanceProgram
{
    public Dictionary<GuidanceMode, IGuidanceMode> Modes { get; set; } = new();
    public Dictionary<GuidanceMode, IGuidanceTarget> Targets { get; set; } = new();
    public GuidanceMode ActiveMode { get; set; }
    public Vehicle Vehicle { get; set; }
    public Simulator Simulator { get; set; }
    public Vector3? Steering { get; set; }
    public bool StagingFlag { get; set; }
    protected int _lastStageCount;
    public float Dt { get; set; } = 1.0f;
    private List<GuidanceMode>? _modeSequence;

    public GuidanceProgram(Dictionary<GuidanceMode, IGuidanceTarget> targets, Vehicle veh, Simulator sim, Mission mission)
    {
        Vehicle = veh;
        _lastStageCount = veh.Stages.Count();
        Simulator = sim;
        Targets = targets;
        Dt = mission.Guidance.dt;
        
        // Try to build from dynamic configuration, fallback to defaults
        BuildModesFromMission(mission);
        ActiveMode = GuidanceMode.Prelaunch;
    }

    private void BuildModesFromMission(Mission mission)
    {
        if (mission.Guidance.programConfig?.modes != null)
        {
            // Store the sequence for navigation
            if (mission.Guidance.programConfig.sequence != null)
            {
                _modeSequence = mission.Guidance.programConfig.sequence
                    .Where(s => Enum.TryParse<GuidanceMode>(s, out _))
                    .Select(s => Enum.Parse<GuidanceMode>(s))
                    .ToList();
            }

            // Build ONLY from dynamic configuration - no defaults
            foreach (var modeConfig in mission.Guidance.programConfig.modes)
            {
                if (Enum.TryParse<GuidanceMode>(modeConfig.Key, out var guidanceMode))
                {
                    var config = GuidanceModeConfig.FromJson(modeConfig.Value.type, modeConfig.Value.config);
                    var mode = GuidanceModeFactory.CreateMode(modeConfig.Value.type, config);
                    Modes[guidanceMode] = mode;
                }
            }

            // Set initial mode from sequence if available
            var firstMode = mission.Guidance.programConfig.sequence?.FirstOrDefault();
            if (firstMode != null && Enum.TryParse<GuidanceMode>(firstMode, out var initialMode))
            {
                ActiveMode = initialMode;
            }
        }
        else
        {
            // Only fallback to defaults if NO programConfig exists
            BuildDefaultModes();
        }
    }

    private void BuildDefaultModes()
    {
        Modes[GuidanceMode.Prelaunch] = new PreLaunchMode();
        Modes[GuidanceMode.Ascent] = new GravityTurnMode();
        Modes[GuidanceMode.OrbitInsertion] = new UpfgMode();
        Modes[GuidanceMode.FinalBurn] = new FinalMode();
        Modes[GuidanceMode.Idle] = new IdleMode();
        
        // Initialize with default configs
        Modes[GuidanceMode.Prelaunch].Initialize(new PreLaunchModeConfig());
        Modes[GuidanceMode.Ascent].Initialize(new GravityTurnModeConfig());
        Modes[GuidanceMode.OrbitInsertion].Initialize(new UpfgModeConfig());
        Modes[GuidanceMode.FinalBurn].Initialize(new FinalModeConfig());
        Modes[GuidanceMode.Idle].Initialize(new IdleModeConfig());
    }

    public void Step()
    {
        var mode = Modes[ActiveMode];
        var tgt = Targets.ContainsKey(ActiveMode) ? Targets[ActiveMode] : null;
        // Propagate staging flag to the mode
        mode.StagingFlag = this.StagingFlag;
        var nextMode = mode.Step(Simulator, tgt, Vehicle, this);
        // Reset after use
        this.StagingFlag = false;

        Vector3? newsteering = mode.GetSteering();
        if (newsteering != null)
        {
            Steering = newsteering;
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
        return Steering ?? Vector3.Zero;
    }

    public GuidanceMode? GetNextModeInSequence(GuidanceMode currentMode)
    {
        if (_modeSequence == null) return null;
        
        var currentIndex = _modeSequence.IndexOf(currentMode);
        if (currentIndex >= 0 && currentIndex < _modeSequence.Count - 1)
        {
            return _modeSequence[currentIndex + 1];
        }
        return null;
    }
}

public class AscentProgram : GuidanceProgram
{
    public AscentProgram(Dictionary<GuidanceMode, IGuidanceTarget> targets, Vehicle veh, Simulator sim, Mission mission)
        : base(targets, veh, sim, mission)
    {
        // Override specific configuration for ascent program
        var upfgConfig = new UpfgModeConfig { ModeKey = 2 };
        Modes[GuidanceMode.OrbitInsertion].Initialize(upfgConfig);
    }
}

// Configuration classes for different guidance modes
public abstract class GuidanceModeConfig 
{
    public static GuidanceModeConfig FromJson(string modeType, JsonElement configElement)
    {
        return modeType switch
        {
            "PreLaunchMode" => JsonSerializer.Deserialize<PreLaunchModeConfig>(configElement.GetRawText()) ?? new PreLaunchModeConfig(),
            "GravityTurnMode" => JsonSerializer.Deserialize<GravityTurnModeConfig>(configElement.GetRawText()) ?? new GravityTurnModeConfig(),
            "UpfgMode" => JsonSerializer.Deserialize<UpfgModeConfig>(configElement.GetRawText()) ?? new UpfgModeConfig(),
            "FinalMode" => JsonSerializer.Deserialize<FinalModeConfig>(configElement.GetRawText()) ?? new FinalModeConfig(),
            "IdleMode" => new IdleModeConfig(),
            _ => throw new ArgumentException($"Unknown mode type: {modeType}")
        };
    }
}

public class PreLaunchModeConfig : GuidanceModeConfig
{
    public float Duration { get; set; } = 0.1f;
}

public class GravityTurnModeConfig : GuidanceModeConfig
{
    public float AltitudeThreshold { get; set; } = 30000f;
}

public class UpfgModeConfig : GuidanceModeConfig
{
    public int ModeKey { get; set; } = 1;
    public float ConvergenceThreshold { get; set; } = 5.0f;
}

public class FinalModeConfig : GuidanceModeConfig
{
    public float BurnTime { get; set; } = 5.0f;
}

public class IdleModeConfig : GuidanceModeConfig { }

// Mission configuration classes
public class GuidanceProgramConfig
{
    public List<string>? sequence { get; set; }
    public Dictionary<string, ModeDefinition>? modes { get; set; }
}

public class ModeDefinition
{
    public string type { get; set; } = "";
    public JsonElement config { get; set; }
}

// Factory for creating guidance modes dynamically
public static class GuidanceModeFactory
{
    public static IGuidanceMode CreateMode(string modeType, GuidanceModeConfig config)
    {
        IGuidanceMode mode = modeType switch
        {
            "PreLaunchMode" => new PreLaunchMode(),
            "GravityTurnMode" => new GravityTurnMode(),
            "UpfgMode" => new UpfgMode(),
            "FinalMode" => new FinalMode(),
            "IdleMode" => new IdleMode(),
            _ => throw new ArgumentException($"Unknown mode type: {modeType}")
        };
        
        mode.Initialize(config);
        return mode;
    }
}

