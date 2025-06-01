// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;
using lib;

class Program
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");

        Simulator sim = new Simulator();

        sim.SetVesselStateFromMidair(70, 8000, 0);

        List<float> simX = new List<float>();
        List<float> simY = new List<float>();

        for (int tt = 0; tt <= 10000; tt++)
        {
            sim.StepForward(1000);

            Vector3 r = (Vector3)sim.State["r"];

            simX.Add(r.X);
            simY.Add(r.Y);
        }

        Console.WriteLine(simX);

        ScottPlot.Plot myPlot = new();
        myPlot.Add.Scatter(simX, simY);
        myPlot.SavePng("test.png", 400, 300);

    }


}

