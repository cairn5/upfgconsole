namespace lib;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Mail;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security;
using ScottPlot.LayoutEngines;
using ScottPlot.Rendering.RenderActions;

public class Simulator
{
    public Vehicle SimVehicle { get; set; }
    public Dictionary<string, object> Mission { get; private set; }
    public SimState State { get; private set; }
    public Dictionary<string, Vector3> Iteration { get; private set; }
    public float dt { get; private set; }
    public float dtguidance { get; set; }
    public double simspeed { get; private set; }
    public Vector3 ThrustVector { get; private set; }
    public List<SimState> History { get; set; }


    public Simulator()
    {
        SimVehicle = new Vehicle();
        Mission = new Dictionary<string, object> { };
        State = new SimState();

        State.CalcMiscParams();
        State.CartToKepler();

        Iteration = new Dictionary<string, Vector3>();

        ThrustVector = new Vector3(0, 0, 0);
        dt = 1;

        History = new List<SimState>();

    }

    public void SetVehicle(Vehicle vehiclein)
    {
        SimVehicle = vehiclein;
        State.mass = (float)SimVehicle.CurrentStage.MassTotal;
    }

    public SimState GetVesselState()
    {
        return State;
    }

    public void SetVesselStateFromLatLongAir(Dictionary<string, double> initial)
    {
        double altitude = initial["altitude"];
        double fpa = initial["fpa"];
        double latitude = initial["latitude"];
        double longitude = initial["longitude"];
        double heading = initial["heading"];
        double speed = initial["speed"];

        fpa = Utils.DegToRad(fpa);
        heading = Utils.DegToRad(heading);

        double r = Constants.Re + altitude * 1000;

        State.r = Utils.SphericalToCartesian(latitude, longitude, r);
        State.v = Utils.ComputeVelocity(State.r, speed, fpa, heading);
        State.t = 0;

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void SetVesselStateFromLatLongGround(Dictionary<string, double> initial)
    {
        double latitude = initial["latitude"];
        double longitude = initial["longitude"];

        double r = Constants.Re;

        State.r = Utils.SphericalToCartesian(latitude, longitude, r);
        State.v = Vector3.Zero;
        State = Utils.SurfaceRestECIVelocity(State); //transform for earth's

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void SetTimeStep(float timestep)
    {
        dt = timestep;
    }

    public void SetGuidance(Vector3 thrustvector, Stage stage)
    {
        ThrustVector = thrustvector * (float)stage.Thrust;
    }

    private void CalcAccel()
    {
        Vector3 r21 = State.r;
        Iteration["a"] = Utils.CalcGravVector(Constants.Mu, r21)
        + ThrustVector / State.mass;

    }

    private void CalcVel()
    {
        Iteration["v"] = State.v + (Vector3)Iteration["a"] * dt;
    }

    private void CalcPos()
    {
        Iteration["r"] = State.r + (Vector3)Iteration["v"] * dt;
    }

    private void UpdateStateHistory()
    {

        History.Add((SimState)State.Clone());

        State.r = Iteration["r"];
        State.v = Iteration["v"];
        State.t = State.t + dt;
        State.mass = State.mass - (float)(dt * ThrustVector.Length() / (Constants.g0 * SimVehicle.CurrentStage.Isp));

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void StepForward()
    {
        CalcAccel();
        CalcVel();
        CalcPos();
        UpdateStateHistory();

    }

    public void LoadSimVarsFromJson(string path)
    {
        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("Simulator");

        double startLat = root.GetProperty("startLat").GetDouble();
        double startLong = root.GetProperty("startLong").GetDouble();
        double startGround = root.GetProperty("startGround").GetDouble();
        double airVel = root.GetProperty("airVel").GetDouble();
        double airFpa = root.GetProperty("airFpa").GetDouble();
        float dtlocal = root.GetProperty("dtsim").GetSingle();
        float dtguidancelocal = root.GetProperty("dtguidance").GetSingle();
        double simspeedlocal = root.GetProperty("speed").GetDouble();
        double altitude = root.GetProperty("altitude").GetSingle();

        // Set initial state using these values
        // Convert degrees to radians where needed
        double latitude = Utils.DegToRad(startLat);
        double longitude = Utils.DegToRad(startLong);

        if (startGround == 0)
        {
            double speed = airVel;
            double fpa = airFpa; // Flight path angle in degrees
            double heading = 90; // Default east, can be parameterized

            var initial = new Dictionary<string, double>
            {
                {"altitude", altitude},
                {"fpa", fpa},
                {"latitude", latitude},
                {"longitude", longitude},
                {"heading", heading},
                {"speed", speed}
            };
            SetVesselStateFromLatLongAir(initial);
        }
        if (startGround == 1)
        {

            var initial = new Dictionary<string, double>
            {
                {"latitude", latitude},
                {"longitude", longitude}
            };
            SetVesselStateFromLatLongGround(initial);
        }
        SetTimeStep(dtlocal);
        this.dtguidance = dtguidancelocal; //this should be in vehicle/ guidance really
        this.simspeed = simspeedlocal;
    }


    public void LoadSimVarsFromDash(SimParams simParams)
    {

        // Set initial state using these values
        // Convert degrees to radians where needed
        double latitude = Utils.DegToRad(simParams.StartLat);
        double longitude = Utils.DegToRad(simParams.StartLong);

        if (simParams.StartGround == 0)
        {
            double speed = simParams.AirVel;
            double fpa = simParams.AirFpa; // Flight path angle in degrees
            double heading = 90; // Default east, can be parameterized

            var initial = new Dictionary<string, double>
            {
                {"altitude", simParams.Altitude},
                {"fpa", fpa},
                {"latitude", latitude},
                {"longitude", longitude},
                {"heading", heading},
                {"speed", speed}
            };
            SetVesselStateFromLatLongAir(initial);
        }
        if (simParams.StartGround == 1)
        {

            var initial = new Dictionary<string, double>
            {
                {"latitude", latitude},
                {"longitude", longitude}
            };
            SetVesselStateFromLatLongGround(initial);
        }
        SetTimeStep((float)simParams.dtsim);
        this.simspeed = simParams.Speed;
    }


}

public class SimState
{
    public Vector3 r { get; set; } = new Vector3(0, 0, 0);
    public Vector3 v { get; set; } = new Vector3(0, 0, 0);
    public float t { get; set; } = 0;
    public float mass { get; set; } = 1e5f;
    public Dictionary<string, double> Kepler { get; set; } = new Dictionary<string, double> { };
    public Dictionary<string, double> Misc { get; set; } = new Dictionary<string, double> { };

    public void CalcMiscParams()
    {
        Misc = new Dictionary<string, double>
        {
            {"longitude", Math.Atan2(r.Y, r.X)},
            {"latitude", Math.Atan2(r.Z, Math.Pow(r.X * r.X + r.Y * r.Y, 0.5))},
            {"altitude", Math.Pow(r.X * r.X + r.Y * r.Y + r.Z * r.Z, 0.5) - Constants.Re}
        };
    }

    public object Clone()
    {
        return this.MemberwiseClone();
    }

    public void CartToKepler()
    {

        Vector3 rdot = v;

        double mu = Constants.Mu;

        Vector3 h = Vector3.Cross(r, rdot);
        double hMag = h.Length();

        Vector3 eVec = Vector3.Cross(rdot, h) / Constants.Mu - r / r.Length();
        double eMag = eVec.Length();

        Vector3 n = Vector3.Cross(new Vector3(0, 0, 1), h);
        double nMag = n.Length();

        double i = Math.Acos(h.Z / hMag);

        // True anomaly
        double v_ = Math.Acos(Vector3.Dot(eVec, r) / (eMag * r.Length()));
        if (Vector3.Dot(r, rdot) < 0)
        {
            v_ = 2 * Math.PI - v_;
        }

        // Eccentric anomaly
        double tan_v2 = Math.Tan(v_ / 2);
        double E = 2 * Math.Atan2(tan_v2 * Math.Sqrt(1 - eMag), Math.Sqrt(1 + eMag));

        // RAAN (Longitude of Ascending Node)
        double LAN = Math.Acos(n.X / nMag);
        if (n.Y < 0)
        {
            LAN = 2 * Math.PI - LAN;
        }
        if (nMag == 0)
        {
            LAN = 0;
        }

        // Argument of Periapsis
        double omega = Math.Acos(Vector3.Dot(n, eVec) / (nMag * eMag));
        if (eVec.Z < 0)
        {
            omega = 2 * Math.PI - omega;
        }
        if (nMag == 0)
        {
            omega = 0;
        }

        // Mean anomaly
        double M = E - eMag * Math.Sin(E);

        // Semi-major axis
        double a = 1 / ((2 / r.Length()) - (Vector3.Dot(rdot, rdot) / mu));

        //ap and pe
        double rp = a * (1 - eMag); // periapsis
        double ra = a * (1 + eMag); // apoapsis

        Kepler = new Dictionary<string, double>
        {
            { "a", a },
            { "e", eMag },
            { "ap", ra },
            { "pe", rp },
            { "omega", Utils.RadToDeg(omega) },
            { "LAN", Utils.RadToDeg(LAN) },
            { "i", Utils.RadToDeg(i) },
            { "M", Utils.RadToDeg(M) }

        };
    }

}
