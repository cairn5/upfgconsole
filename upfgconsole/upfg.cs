using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
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

    public void SetupUpfg(Simulator sim, Target mission)
    {
        Vector3 curR = sim.State.r;
        Vector3 curV = sim.State.v;

        Vector3 unitvec = Utils.RodriguesRotation(curR, mission.normal, 20);
        Vector3 desR = unitvec / unitvec.Length() * mission.radius;

        Vector3 tempvec = Vector3.Cross(-mission.normal, desR);
        Vector3 tgoV = mission.velocity * (tempvec / tempvec.Length()) - curV;

        Dictionary <string, double> cser = new Dictionary<string, double>
        {
            {"dtcp", 0 },
            {"xcp", 0 },
            {"A", 0 },
            {"D", 0 },
            {"E", 0 }
        };

        PrevVals.SetVals(cser, new Vector3(0, 0, 0), desR, (float)0.5 * Utils.CalcGravVector(Constants.Mu, curR), 0, sim.State.t, 100, curV, tgoV);
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

    public void SetVals(Dictionary<string, double> cserin,
                        Vector3 rbiasin, Vector3 rdin, Vector3 rgravin,
                        double tbin, double timein, double tgoin, Vector3 vin, Vector3 vgoin)
    {
        Cser = cserin;
        rbias = rbiasin;
        rd = rdin;
        rgrav = rgravin;
        tb = tbin;
        time = timein;
        tgo = tgoin;
        v = vin;
        vgo = vgoin;
                                
    }

}