using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Security;
using ScottPlot.LayoutEngines;

namespace lib;

public class GravityTurn
{
    public double pitchAngle { get; set; } = Utils.DegToRad(1.5f);
    public double pitchTime { get; set; } = 17.0f;
    public double heading { get; set; } = -1;
    public int guidanceMode { get; set; } = 0; // 0 = straight up, 1 = initial pitchover, 2 = following ECEF prograde
    public Vector3 guidance { get; set; } = Vector3.Zero;
    public bool SetupFlag { get; set; } = false;

    public void step(Simulator sim, UPFGTarget target)
    {

        if (!SetupFlag)
        {
            // this.pitchAngle = pitchAngle;
            // this.pitchTime = pitchTime;
            heading = Utils.CalcLaunchAzimuthRotating(sim, target);
            SetupFlag = true;
        }

        if (guidanceMode == 0)
        {
            guidance = Vector3.Cross(Utils.GetEastUnit(sim.State.r), Utils.GetNorthUnit(sim.State.r));

            if (sim.State.t > pitchTime)
            {
                guidanceMode = 1;
            }

        }

        if (guidanceMode == 1)
        {
            // Rotate up vector by pitch angle, then rotate to desired heading
            Vector3 north = Utils.GetNorthUnit(sim.State.r);
            Vector3 east = Utils.GetEastUnit(sim.State.r);
            Vector3 up = -Vector3.Cross(north, east);
            Vector3 transformedVector = Utils.RodriguesRotation(up, east, (float)pitchAngle);

            guidance = Utils.RodriguesRotation(transformedVector, up, -(float)(heading));

            // If dot product betwen thrust vector and velocity vector is less than value, hold prograde
            Vector3 localVelNorm = Vector3.Normalize(Utils.ECItoECEF(sim.State).v);

            float dotProduct = Vector3.Dot(guidance, localVelNorm);
            if (dotProduct > 0.9995f)
            {
                guidanceMode = 2;
            }

        }

        if (guidanceMode == 2)
        {
            guidance = Vector3.Normalize(Utils.ECItoECEF(sim.State).v);
        }

    }
}