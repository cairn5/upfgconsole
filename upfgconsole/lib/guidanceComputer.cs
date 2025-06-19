public enum GuidanceMode
{
    Idle,
    Prelaunch,
    Ascent,
    OrbitInsertion,
    Abort,
}

public interface IGuidanceMode
{
    bool Converged { get; }
    void Setup(Simulator sim, Target tgt);
    GuidanceMode? Run(Simulator sim, Target tgt, Vehicle veh);
    Vector3 GetSteering();
}

public class UpfgMode : IGuidanceMode
{
    private Upfg upfg = new Upfg();

    public bool Converged => upfg.ConvergenceFlag;
    public void Setup(Simulator sim, Target tgt) => upfg.Setup(sim, tgt);
    public GuidanceMode? Run(Simulator sim, Target tgt, Vehicle veh)
    {
        upfg.Run(sim, tgt, veh);
        if (upfg.ConvergenceFlag)
            return GuidanceMode.OrbitInsertion;
        return null;
    }

    public Vector3 GetSteering() => upfg.Steering;

}

public class PreLaunchMode : IGuidanceMode
{
    public bool Converged => true; // Pre-launch is always converged
    public void Setup(Simulator sim, Target tgt) { /* No setup needed */ }
    public GuidanceMode? Run(Simulator sim, Target tgt, Vehicle veh)
        { 
        // Simulate waiting or holding state during pre-launch; no action needed
        System.Threading.Thread.Sleep(100); // Sleep for 100 milliseconds
        // Example: transition to Ascent when ready (replace with real logic)
        return GuidanceMode.Ascent;
        
        }
    public Vector3 GetSteering() => Vector3.Zero; // No steering during pre-launch
}

public class GravityTurnMode : IGuidanceMode
{
    private GravityTurn gravityTurn = new GravityTurn();
    public bool Converged => gravityTurn.ConvergenceFlag;
    public void Setup(Simulator sim, Target tgt) => gravityTurn.Setup(sim, tgt);
    public GuidanceMode? Run(Simulator sim, Target tgt, Vehicle veh)
    {
        gravityTurn.Run(sim, tgt, veh);
        if (gravityTurn.ConvergenceFlag)
            return GuidanceMode.Idle; // Or next mode
        return null;
    }
    public Vector3 GetSteering() => gravityTurn.Steering;
}

public class GuidanceComputer
{
    public Dictionary<GuidanceMode, IGuidanceMode> Modes { get; } = new();
    public HashSet<GuidanceMode> ActiveModes { get; } = new();
    public Target Target { get; set; }
    public Vehicle Vehicle { get; set; }
    public Simulator Simulator { get; set; }

    public GuidanceComputer(Target tgt, Vehicle veh, Simulator sim)
    {
        Target = tgt;
        Vehicle = veh;
        Simulator = sim;
        Modes[GuidanceMode.Prelaunch] = new PreLaunchMode();
        Modes[GuidanceMode.Ascent] = new UpfgMode();
        Modes[GuidanceMode.OrbitInsertion] = new GravityTurnMode();
        // Add more modes as needed
        // Example: Start with both Ascent and OrbitInsertion active
        ActiveModes.Add(GuidanceMode.Ascent);
        ActiveModes.Add(GuidanceMode.OrbitInsertion);
    }

    public void SetupModes()
    {
        foreach (var mode in Modes.Values)
            mode.Setup(Simulator, Target);
    }

    public void Step()
    {
        // Run all active modes
        var toAdd = new List<GuidanceMode>();
        var toRemove = new List<GuidanceMode>();
        foreach (var modeKey in ActiveModes)
        {
            var mode = Modes[modeKey];
            var nextMode = mode.Run(Simulator, Target, Vehicle);
            if (nextMode.HasValue && !ActiveModes.Contains(nextMode.Value))
                toAdd.Add(nextMode.Value);
            // Optionally remove mode if converged (or keep running for background convergence)
            // if (mode.Converged) toRemove.Add(modeKey);
        }
        foreach (var m in toAdd) ActiveModes.Add(m);
        foreach (var m in toRemove) ActiveModes.Remove(m);
    }

    public Vector3 GetCurrentSteering()
    {
        // Example: prefer GravityTurn if active, else UPFG, else zero
        if (ActiveModes.Contains(GuidanceMode.OrbitInsertion))
            return Modes[GuidanceMode.OrbitInsertion].GetSteering();
        if (ActiveModes.Contains(GuidanceMode.Ascent))
            return Modes[GuidanceMode.Ascent].GetSteering();
        return Vector3.Zero;
    }
}


