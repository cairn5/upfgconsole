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
    
        public void CalculateSphericalAAS_Returns_Expected_0deg()
    {
        float a = (float)Utils.DegToRad(0); //lat
        float beta = (float)Utils.DegToRad(90);
        float alpha = (float)Utils.DegToRad(10); //inclination
        (float b, float c) = Utils.CalculateSphericalAAS(a, beta, alpha);


        //Assert.Equal(-Math.PI / 2, c);
        Assert.Equal(0, b, 2);
        
    }

    
}
