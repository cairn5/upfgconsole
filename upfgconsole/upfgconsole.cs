// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;
using lib;
using System.Reflection;

class Handler
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");

        Simulator sim = new Simulator();
        sim.SetVesselStateFromMidair(20, 2000, 45);


        Target tgt = new Target();

        Dictionary<string, float> desOrbit = new Dictionary<string, float>{
            {"pe", 200 },
            {"ap", 200},
            {"inc", 45}
        };

        tgt.SetTarget(desOrbit, sim);

        Upfg guidance = new Upfg();

        guidance.SetupUpfg(sim, tgt);

        
        List<float> simX = new List<float>();
        List<float> simY = new List<float>();

        for (int tt = 0; tt <= 10000; tt++)
        {
            sim.StepForward(1000);

            Vector3 r = sim.State.r;

            simX.Add(r.X);
            simY.Add(r.Y);
        }

        Console.WriteLine(simX);

        ScottPlot.Plot myPlot = new();
        myPlot.Add.Scatter(simX, simY);
        myPlot.SavePng("test.png", 400, 300);

        Dictionary<string, double> kepler = sim.State.Kepler;

        Utils.PlotOrbit(kepler);
    }



}

