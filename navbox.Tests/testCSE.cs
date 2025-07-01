using System;
using Xunit;
using System.Numerics;
using System.Collections.Generic;
using lib;

namespace lib_tests
{
    public class OrbitalMechanicsTests
    {
        [Fact]
        public void USS_Converges_For_ValidInput()
        {
            double result = OrbitalMechanics.USS(1.0, 0.1, 50);
            Assert.InRange(result, 0.0, 1.0);
        }

        [Fact]
        public void QCF_Returns_ExpectedValue_ForLowW()
        {
            double w = 0.5;
            double q = OrbitalMechanics.QCF(w);
            Assert.True(q > 0);
        }

        [Fact]
        public void QCF_Returns_ExpectedValue_ForHighW()
        {
            double w = 50;
            double q = OrbitalMechanics.QCF(w);
            Assert.True(q > 0);
        }

        [Fact]
        public void KTTI_Returns_CorrectTuple()
        {
            double x = 0.5;
            double s0s = 0.1;
            double alpha = 0.2;
            int kmax = 50;

            var (t, A, D, E) = OrbitalMechanics.KTTI(x, s0s, alpha, kmax);
            Assert.True(t > 0);
            Assert.True(A > 0);
            Assert.True(D > 0);
            Assert.True(E > 0);
        }

        [Fact]
        public void SI_ReturnsExpectedDeltaX()
        {
            var result = OrbitalMechanics.SI(0.01, 0.5, 100, 0.2, 90, 1.0, 110);
            Assert.True(Math.Abs(result.dxs) > 0);
        }

        [Fact]
        public void KIL_RefinesXGuess()
        {
            var result = OrbitalMechanics.KIL(
                imax: 10,
                dts: 100,
                xguess: 0.5,
                dtguess: 90,
                xmin: 0.2,
                dtmin: 80,
                xmax: 1.0,
                dtmax: 110,
                s0s: 0.1,
                a: 0.2,
                kmax: 50,
                A: 0.1,
                D: 0.1,
                E: 0.1
            );

            Assert.True(result.xguess > 0);
            Assert.True(result.dtguess > 0);
        }

        [Fact]
        public void CSEroutine_ReturnsReasonableValues()
        {
            var r0 = new Vector3(7000e3f, 0, 0);
            var v0 = new Vector3(0, 7.5e3f, 0);
            double dt = 60.0;

            var last = new Dictionary<string, double>()
            {
                { "dtcp", 0 },
                { "xcp", 0 },
                { "A", 0.1 },
                { "D", 0.1 },
                { "E", 0.1 }
            };

            var (r, v, updated) = OrbitalMechanics.CSEroutine(r0, v0, dt, last);

            Assert.True(r.Length() > 0);
            Assert.True(v.Length() > 0);
            Assert.True(updated.ContainsKey("A"));
            Assert.True(updated["dtcp"] > 0);
        }

        [Fact]
        public void CSEroutine_ReturnsSpecificValues()
        {
            var r0 = new Vector3(6000e3f, 0, 2000);
            var v0 = new Vector3(0, 7.5e3f, 0);
            double dt = 60.0;

            var last = new Dictionary<string, double>()
            {
                { "dtcp", 0 },
                { "xcp", 0 },
                { "A", 0.0 },
                { "D", 0.0 },
                { "E", 0.0 }
            };

            var (r, v, updated) = OrbitalMechanics.CSEroutine(r0, v0, dt, last);
            Vector3 rexp = new(5980076.5f, 449498.0f, 1993.4f);
            Vector3 vexp = new(-663.9f, 7475.1f, -0.2213f);

            Assert.Equal(rexp.X, r.X, 1);
            Assert.Equal(rexp.Y, r.Y, 1);
            Assert.Equal(rexp.Z, r.Z, 1);

            Assert.Equal(vexp.X, v.X, 1);
            Assert.Equal(vexp.Y, v.Y, 1);
            Assert.Equal(vexp.Z, v.Z, 1);

        }
    }
}
