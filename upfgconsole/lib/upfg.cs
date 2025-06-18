using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Security;
using ScottPlot.LayoutEngines;

namespace lib;

public class Upfg
{
    public UPFGState PrevVals { get; private set; }
    public UPFGState CurrentVals { get; private set; }
    public bool ConvergenceFlag { get; private set; }
    public Vector3 Steering { get; private set; }

    public Upfg()
    {
        PrevVals = new UPFGState();
        CurrentVals = new UPFGState();
        ConvergenceFlag = false;
        Steering = new Vector3(0, 0, 0);
    }

    public void Setup(Simulator sim, Target mission)
    {
        Vector3 curR = sim.State.r;
        Vector3 curV = sim.State.v;

        Vector3 unitvec = Utils.RodriguesRotation(curR, mission.normal, Utils.DegToRad(20));
        Vector3 desR = unitvec / unitvec.Length() * mission.radius;

        Vector3 tempvec = Vector3.Cross(mission.normal, desR);
        Vector3 tgoV = mission.velocity * (tempvec / tempvec.Length()) - curV;

        Dictionary<string, double> cser = new Dictionary<string, double>
        {
            {"dtcp", 0 },
            {"xcp", 0 },
            {"A", 0 },
            {"D", 0 },
            {"E", 0 }
        };

        PrevVals.SetVals(cser, new Vector3(0, 0, 0), desR, (float)0.5 * Utils.CalcGravVector(Constants.Mu, curR), 0, sim.State.t, 100, curV, tgoV);
    }

    public void Run(Simulator sim, Target mission, Vehicle vehicle)
    {
        double gamma = mission.fpa;
        Vector3 iy = -mission.normal;
        double rdval = mission.radius;
        double vdval = mission.velocity;

        Vector3 r_ = sim.State.r;
        Vector3 v_ = sim.State.v;
        double t = sim.State.t;
        double m = sim.State.mass;

        Dictionary<string, double> cser = PrevVals.Cser;
        Vector3 rbias = PrevVals.rbias;
        Vector3 rd = PrevVals.rd;
        Vector3 rgrav = PrevVals.rgrav;
        double tp = PrevVals.time;
        Vector3 vprev = PrevVals.v;
        Vector3 vgo = PrevVals.vgo;

        // 1 - Initialization
        int n = vehicle.Stages.Count;

        List<int> SM = new List<int>();
        List<double> aL = new List<double>();
        List<double> md = new List<double>();
        List<double> ve = new List<double>();
        List<double> fT = new List<double>();
        List<double> aT = new List<double>();
        List<double> tu = new List<double>();
        List<double> tb = new List<double>();
                
        for (int i = 0; i < n; i++)
        {
            var stage = vehicle.Stages[i];

            double massflow = stage.Thrust / (stage.Isp * Constants.g0);

            SM.Add(stage.Mode);
            aL.Add(stage.GLim * Constants.g0);
            fT.Add(stage.Thrust);
            md.Add(massflow);
            ve.Add(stage.Isp * Constants.g0);
            aT.Add(stage.Thrust / stage.MassTotal);
            tu.Add(ve[i] / aT[i]);
            tb.Add((stage.MassTotal - stage.MassDry) / massflow);
        }

        // 2 accelerations
        double dt = t - tp;
        Vector3 dvsensed = v_ - vprev;
        vgo -= dvsensed;
        tb[0] -= PrevVals.tb;

        // 3 burn time
        // double[] aT = new double[n];
        // double[] tu = new double[n];

        if (SM[0] == 1)
        {
            aT[0] = fT[0] / m;
        }
        else if (SM[0] == 2)
        {
            aT[0] = aL[0];
        }

        tu[0] = ve[0] / aT[0];
        double L = 0;
        List<double> Li = new List<double>();

        for (int i = 0; i < n - 1; i++)
        {
            if (SM[i] == 1)
                Li.Add(ve[i] * Math.Log(tu[i] / (tu[i] - tb[i])));
            else if (SM[i] == 2)
                Li.Add(aL[i] * tb[i]);

            L += Li[i];
        }

        Li.Add((vgo).Length() - L);

        List<double> tgoi = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (SM[0] == 1)
                tb[i] = tu[i] * (1 - Math.Exp(-Li[i] / ve[i]));
            else if (SM[0] == 2)
                tb[i] = Li[i] / aL[i];

            if (i == 0)
                tgoi.Add(tb[i]);
            else
                tgoi.Add(tgoi[i - 1] + tb[i]);
        }

        double tgo = tgoi[n - 1];

        // 4 - Thrust integrals
        L = 0;
        double J = 0, S = 0, Q_ = 0, H = 0, P = 0;

        List<double> Ji = new List<double>(new double[n]);
        List<double> Si = new List<double>(new double[n]);
        List<double> Qi = new List<double>(new double[n]);
        List<double> Pi = new List<double>(new double[n]);

        for (int i = 0; i < n; i++)
        {
            double tgoi1 = (i == 0) ? 0 : tgoi[i - 1];

            if (SM[i] == 1) // Constant thrust mode
            {
                Ji[i] = tu[i] * Li[i] - ve[i] * tb[i];
                Si[i] = -Ji[i] + Li[i] * tb[i];
                Qi[i] = Si[i] * (tu[i] + tgoi1) - 0.5 * ve[i] * tb[i] * tb[i];
                Pi[i] = Qi[i] * (tu[i] + tgoi1) - 0.5 * ve[i] * tb[i] * tb[i] * (tb[i] / 3 + tgoi1);
            }
            else if (SM[i] == 2) // Constant acceleration mode
            {
                Ji[i] = 0.5 * Li[i] * tb[i];
                Si[i] = Ji[i];
                Qi[i] = Si[i] * (tb[i] / 3 + tgoi1);
                Pi[i] = (1.0 / 6.0) * Si[i] * (tgoi[i] * tgoi[i] + 2 * tgoi[i] * tgoi1 + 3 * tgoi1 * tgoi1);
            }

            // Common adjustments for both modes
            Ji[i] += Li[i] * tgoi1;
            Si[i] += L * tb[i];
            Qi[i] += J * tb[i];
            Pi[i] += H * tb[i];

            // Accumulate totals
            L += Li[i];
            J += Ji[i];
            S += Si[i];
            Q_ += Qi[i];
            P += Pi[i];
            H = J * tgoi[i] - Q_;
        }


        // 5
        Vector3 lambda_vec = Vector3.Normalize(vgo);

        if (PrevVals.tgo > 0)
        {
            rgrav = (float)Math.Pow(tgo / PrevVals.tgo, 2) * rgrav;
        }

        Vector3 rgo = rd - (r_ + v_ * (float)tgo + rgrav);

        Vector3 iz = Vector3.Normalize(Vector3.Cross(rd, iy));
        Vector3 rgoxy = rgo - Vector3.Dot(iz, rgo) * iz;
        double rgoz = (S - Vector3.Dot(lambda_vec, rgoxy)) / Vector3.Dot(lambda_vec, iz);
        rgo = rgoxy + (float)rgoz * iz + rbias;

        double lambdade = Q_ - S * J / L;
        Vector3 lambdadot = (rgo - (float)S * lambda_vec) / (float)lambdade;
        Vector3 iF_ = lambda_vec - lambdadot * (float)(J / L);
        iF_ = Vector3.Normalize(iF_);

        double phi = Math.Acos(Math.Clamp(Vector3.Dot(iF_, lambda_vec) / (iF_.Length() * lambda_vec.Length()), -1.0, 1.0));
        double phidot = -phi * L / J;

        Vector3 vthrust = (float)(L - 0.5 * L * phi * phi - J * phi * phidot - 0.5 * H * phidot * phidot) * lambda_vec;

        double rthrustMag = S - 0.5 * S * phi * phi - Q_ * phi * phidot - 0.5 * P * phidot * phidot;
        Vector3 rthrust = (float)rthrustMag * lambda_vec - ((float)(S * phi + Q_ * phidot) * (lambdadot / lambdadot.Length()));

        Vector3 vbias = vgo - vthrust;
        rbias = rgo - rthrust;

        // 6
        Vector3 UP = Vector3.Normalize(r_);
        Vector3 EAST = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), UP));

        // 7
        Vector3 rc1 = r_ - 0.1f * rthrust - (float)(tgo / 30.0) * vthrust;
        Vector3 vc1 = v_ + 1.2f * rthrust / (float)tgo - 0.1f * vthrust;

        // External estimation routine
        (Vector3 rend, Vector3 vend, Dictionary<string, double> cserOut) = OrbitalMechanics.CSEroutine(rc1, vc1, tgo, cser);
        cser = cserOut;

        rgrav = rend - rc1 - vc1 * (float)tgo;
        Vector3 vgrav = vend - vc1;

        // 8
        Vector3 rp = r_ + v_ * (float)tgo + rgrav + rthrust;
        rp -= Vector3.Dot(rp, iy) * iy;

        rd = (float)rdval * rp / rp.Length();

        Vector3 ix = Vector3.Normalize(rd);
        iz = Vector3.Cross(ix, iy);

        Vector3 vv1 = new Vector3(ix.X, iy.X, iz.X);
        Vector3 vv2 = new Vector3(ix.Y, iy.Y, iz.Y);
        Vector3 vv3 = new Vector3(ix.Z, iy.Z, iz.Z);

        Vector3 vop = new Vector3((float)Math.Sin(gamma), 0, (float)Math.Cos(gamma));
        Vector3 vd = new Vector3(
            Vector3.Dot(vv1, vop),
            Vector3.Dot(vv2, vop),
            Vector3.Dot(vv3, vop)
        ) * (float)vdval;

        vgo = vd - v_ - vgrav + vbias;

        CurrentVals.SetVals(cser, rbias, rd, rgrav, PrevVals.tb + dt, t, tgo, v_, vgo);

        CheckConvergence();

        PrevVals = CurrentVals;

        Steering = iF_;

    }

    public void CheckConvergence()
    {
        double tgodiff = (CurrentVals.tgo - PrevVals.tgo) / PrevVals.tgo;
        if (Math.Abs(tgodiff) < 0.01)
        {
            ConvergenceFlag = true;
            // Console.WriteLine("UPFG CONVERGED");
        }
        ;
    }

}

public class UPFGState
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