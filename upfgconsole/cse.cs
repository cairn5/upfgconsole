using System;
using System.Collections.Generic;
using System.Linq;

namespace lib;
public class OrbitalMechanics
{
    // Global gravity parameter (mu)
    public static double mu = 398600.4418e9; // Example for Earth (m^3/s^2)

    // Entry point function: CSEroutine
    public static (double[] r, double[] v, Dictionary<string, object> last) CSEroutine(double[] r0, double[] v0, double dt, Dictionary<string, object> last)
    {
        double dtcp = last.ContainsKey("dtcp") && (double)last["dtcp"] != 0 ? (double)last["dtcp"] : dt;

        double xcp = (double)last["xcp"];
        double x = xcp;
        double A = (double)last["A"];
        double D = (double)last["D"];
        double E = (double)last["E"];

        int kmax = 10;
        int imax = 10;

        double f0 = dt >= 0 ? 1 : -1;

        int n = 0;
        double r0m = Norm(r0);

        double f1 = f0 * Math.Sqrt(r0m / mu);
        double f2 = 1 / f1;
        double f3 = f2 / r0m;
        double f4 = f1 * r0m;
        double f5 = f0 / Math.Sqrt(r0m);
        double f6 = f0 * Math.Sqrt(r0m);

        double[] ir0 = Divide(r0, r0m);
        double[] v0s = Multiply(v0, f1);
        double sigma0s = Dot(ir0, v0s);
        double b0 = Dot(v0s, v0s) - 1;
        double alphas = 1 - b0;

        double xguess = f5 * x;
        double xlast = f5 * xcp;
        double xmin = 0;
        double dts = f3 * dt;
        double dtlast = f3 * dtcp;
        double dtmin = 0;

        double xmax = 2 * Math.PI / Math.Sqrt(Math.Abs(alphas));

        double dtmax = 0, xP = 0, Ps = 0;

        if (alphas > 0)
        {
            dtmax = xmax / alphas;
            xP = xmax;
            Ps = dtmax;
            while (dts >= Ps)
            {
                n++;
                dts -= Ps;
                dtlast -= Ps;
                xguess -= xP;
                xlast -= xP;
            }
        }
        else
        {
            var ktti = KTTI(xmax, sigma0s, alphas, kmax);
            dtmax = ktti.t;
            if (dtmax < dts)
            {
                while (dtmax < dts)
                {
                    dtmin = dtmax;
                    xmin = xmax;
                    xmax = 2 * xmax;
                    ktti = KTTI(xmax, sigma0s, alphas, kmax);
                    dtmax = ktti.t;
                }
            }
        }

        if (xmin >= xguess || xguess >= xmax)
            xguess = 0.5 * (xmin + xmax);

        var kttiGuess = KTTI(xguess, sigma0s, alphas, kmax);
        double dtguess = kttiGuess.t;

        if (dts < dtguess)
        {
            if (xguess < xlast && xlast < xmax && dtguess < dtlast && dtlast < dtmax)
            {
                xmax = xlast;
                dtmax = dtlast;
            }
        }
        else
        {
            if (xmin < xlast && xlast < xguess && dtmin < dtlast && dtlast < dtguess)
            {
                xmin = xlast;
                dtmin = dtlast;
            }
        }

        var kil = KIL(imax, dts, xguess, dtguess, xmin, dtmin, xmax, dtmax, sigma0s, alphas, kmax, A, D, E);
        xguess = kil.xguess;
        dtguess = kil.dtguess;
        A = kil.A;
        D = kil.D;
        E = kil.E;

        double rs = 1 + 2 * (b0 * A + sigma0s * D * E);
        double b4 = 1 / rs;

        double xc, dtc;
        if (n > 0)
        {
            xc = f6 * (xguess + n * xP);
            dtc = f4 * (dtguess + n * Ps);
        }
        else
        {
            xc = f6 * xguess;
            dtc = f4 * dtguess;
        }

        last["dtcp"] = dtc;
        last["xcp"] = xc;
        last["A"] = A;
        last["D"] = D;
        last["E"] = E;

        double F = 1 - 2 * A;
        double Gs = 2 * (D * E + sigma0s * A);
        double Fts = -2 * b4 * D * E;
        double Gt = 1 - 2 * b4 * A;

        double[] r = Add(Multiply(ir0, F * r0m), Multiply(v0s, Gs * r0m));
        double[] v = Add(Multiply(ir0, Fts * f2), Multiply(v0s, Gt * f2));

        return (r, v, last);
    }

    // KTTI Function
    public static (double t, double A, double D, double E) KTTI(double xarg, double s0s, double a, int kmax)
    {
        double u1 = USS(xarg, a, kmax);
        double zs = 2 * u1;
        double E = 1 - 0.5 * a * zs * zs;
        double w = Math.Sqrt(Math.Max(0.5 + E / 2, 0));
        double D = w * zs;
        double A = D * D;
        double B = 2 * (E + s0s * D);
        double Q = QCF(w);
        double t = D * (B + A * Q);
        return (t, A, D, E);
    }

    // USS Function
    public static double USS(double xarg, double a, int kmax)
    {
        double du1 = xarg / 4;
        double u1 = du1;
        double f7 = -a * du1 * du1;
        int k = 3;
        while (k < kmax)
        {
            du1 = f7 * du1 / (k * (k - 1));
            double u1old = u1;
            u1 += du1;
            if (u1 == u1old) break;
            k += 2;
        }
        return u1;
    }

    // QCF Function
    public static double QCF(double w)
    {
        double xq;
        if (w < 1)
            xq = 21.04 - 13.04 * w;
        else if (w < 4.625)
            xq = (5.0 / 3.0) * (2 * w + 5);
        else if (w < 13.846)
            xq = (10.0 / 7.0) * (w + 12);
        else if (w < 44)
            xq = 0.5 * (w + 60);
        else if (w < 100)
            xq = 0.25 * (w + 164);
        else
            xq = 70;

        double y = (w - 1) / (w + 1);
        int j = (int)Math.Floor(xq);
        double b = y / (1 + (j - 1) / (j + 2.0) * (1 - 0));
        while (j > 2)
        {
            j--;
            b = y / (1 + (j - 1) / (j + 2.0) * (1 - b));
        }

        double Q = 1 / (w * w) * (1 + (2 - b / 2) / (3 * w * (w + 1)));
        return Q;
    }

    // KIL Function
    public static (double xguess, double dtguess, double A, double D, double E) KIL(int imax, double dts, double xguess, double dtguess, double xmin, double dtmin, double xmax, double dtmax, double s0s, double a, int kmax, double A, double D, double E)
    {
        int i = 1;
        while (i < imax)
        {
            double dterror = dts - dtguess;
            if (Math.Abs(dterror) < 1e-6) break;

            var si = SI(dterror, xguess, dtguess, xmin, dtmin, xmax, dtmax);
            double dxs = si.dxs;
            xmin = si.xmin;
            dtmin = si.dtmin;
            xmax = si.xmax;
            dtmax = si.dtmax;

            double xold = xguess;
            xguess += dxs;
            if (xguess == xold) break;

            double dtold = dtguess;
            var ktti = KTTI(xguess, s0s, a, kmax);
            dtguess = ktti.t;
            A = ktti.A;
            D = ktti.D;
            E = ktti.E;

            if (dtguess == dtold) break;

            i++;
        }
        return (xguess, dtguess, A, D, E);
    }

    // SI Function
    public static (double dxs, double xmin, double dtmin, double xmax, double dtmax) SI(double dterror, double xguess, double dtguess, double xmin, double dtmin, double xmax, double dtmax)
    {
        double etp = 1e-6;
        double dtminp = dtguess - dtmin;
        double dtmaxp = dtguess - dtmax;
        double dxs = 0;

        if (Math.Abs(dtminp) < etp || Math.Abs(dtmaxp) < etp)
        {
            dxs = 0;
        }
        else
        {
            if (dterror < 0)
            {
                dxs = (xguess - xmax) * (dterror / dtmaxp);
                if ((xguess + dxs) <= xmin)
                    dxs = (xguess - xmin) * (dterror / dtminp);
                xmax = xguess;
                dtmax = dtguess;
            }
            else
            {
                dxs = (xguess - xmin) * (dterror / dtminp);
                if ((xguess + dxs) >= xmax)
                    dxs = (xguess - xmax) * (dterror / dtmaxp);
                xmin = xguess;
                dtmin = dtguess;
            }
        }

        return (dxs, xmin, dtmin, xmax, dtmax);
    }

    // --- Helper Methods ---

    public static double Norm(double[] v) => Math.Sqrt(v.Select(x => x * x).Sum());

    public static double[] Divide(double[] v, double scalar) => v.Select(x => x / scalar).ToArray();

    public static double[] Multiply(double[] v, double scalar) => v.Select(x => x * scalar).ToArray();

    public static double[] Add(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();

    public static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
}
