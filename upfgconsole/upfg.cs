using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;

namespace lib;

public class Upfg
{
    public PreviousVals PrevVals { get; private set; }
    public bool ConvergenceFlag { get; private set; }

    public Upfg()
    {
        PrevVals = new PreviousVals();
        ConvergenceFlag = false;
    }

    public void SetupUpfg(Simulator sim)
    {
        Vector3 curR = sim.State.r;
    }
}

public class PreviousVals
{
    public Dictionary<string, double> Cser { get; private set; }
    public Vector3 rbias { get; private set; }
    public Vector3 rd { get; private set; }
    public Vector3 rgrav { get; private set; }
    public double tb { get; private set; }
    public double time { get; private set; }
    public double tgo { get; private set; }
    public Vector3 v { get; private set; }
    public Vector3 vgo { get; private set; }

}