// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;
using lib;
using System.Reflection;
using System.Runtime.InteropServices;

class Handler
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");

       
        float lat = 28.5f;
        string path = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";

        MissionConfig mission = Utils.ReadMission(path);

        Vehicle veh = Vehicle.FromStages(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;

        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(desOrbit["inc"])) / Math.Cos(Utils.DegToRad(lat)));

        Dictionary<string, double> initial = new Dictionary<string, double>
        {
            {"altitude", 45 },
            {"fpa", 50 },
            {"speed", 2400 },
            {"latitude", lat},
            {"longitude", 0 },
            {"heading", Utils.RadToDeg(azimuth) }

        };

        
        Simulator sim = new Simulator();
        sim.SetVesselStateFromLatLong(initial);
        sim.SetVehicle(veh);

        sim.State.mass = (float)veh.Stages[0].MassTotal;

        Target tgt = new Target();
        tgt.SetTarget(desOrbit, sim);
        
        Upfg guidance = new Upfg();
        guidance.Setup(sim, tgt);

        sim.StepForward();

        double trem = 2;

        Utils.PrintParamHeader();


        int iter = 0;

        while (guidance.PrevVals.tgo > trem)
        {
            
            if (iter > 1000)
            {
                Console.WriteLine("UPFG FAILED - CONVERGENCE FAILURE");
                return;  //throw new Exception("Exceeded max iterations");
            }

            guidance.Run(sim, tgt, veh);

            Utils.PrintParams(guidance);


            // Console.WriteLine(sim.State.mass);

            if (guidance.ConvergenceFlag)
            {
                sim.SetGuidance(guidance.Steering, veh.Stages[0]);
                sim.StepForward();
            }


            if (sim.State.mass < sim.SimVehicle.CurrentStage.MassDry)
            {
                if (veh.Stages.Count() > 1)
                {
                    veh.AdvanceStage();
                    sim.SetVehicle(veh);

                    Console.WriteLine("STAGING");
                }
                else
                {
                    Console.WriteLine("UPFG FAILED - INSUFFICIENT DV");
                    return;  
                }
                
                
            }
        

            iter++;

        }

        Utils.PlotTrajectory(sim);

        Dictionary<string, double> kepler = sim.State.Kepler;

        Console.WriteLine(kepler["e"]);

        Utils.PlotOrbit(kepler);
    }

    
}

