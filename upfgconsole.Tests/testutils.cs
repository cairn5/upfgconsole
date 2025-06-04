using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using lib;

namespace lib_tests;

public class UtilsTests
{
    [Fact]
    public void CalculateSphericalAAS_Returns_Expected_90deg()
    {
        float a = (float)Utils.DegToRad(10); //lat
        float beta = (float)Utils.DegToRad(90);
        float alpha = (float)Utils.DegToRad(10); //inclination
        (float b, float c) = Utils.CalculateSphericalAAS(a, beta, alpha);


        //Assert.Equal(-Math.PI / 2, c);
        Assert.Equal(Math.PI / 2, b, 2);

    }

    [Fact]
    public void CalculateSphericalAAS_Returns_Expected_0deg()
    {
        float a = (float)Utils.DegToRad(0); //lat
        float beta = (float)Utils.DegToRad(90);
        float alpha = (float)Utils.DegToRad(10); //inclination
        (float b, float c) = Utils.CalculateSphericalAAS(a, beta, alpha);


        //Assert.Equal(-Math.PI / 2, c);
        Assert.Equal(0, b, 2);

    }

    [Fact]
    public void GetEastUnit_Returns_Expected()
    {
        Vector3 pos = new(1, 0, 0);
        Vector3 exp = new(0, 1, 0);

        Vector3 east = Utils.GetEastUnit(pos);

        Assert.Equal(exp, east);

    }
    
    [Fact]
    public void GetNorthUnit_Returns_Expected()
    {
        Vector3 pos = new(1, 0, 0);
        Vector3 exp = new(0, 0, 1);

        Vector3 north = Utils.GetNorthUnit(pos);

        Assert.Equal(exp, north);

    }

}
