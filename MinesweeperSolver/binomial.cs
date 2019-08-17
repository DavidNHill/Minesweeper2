using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MinesweeperSolver {


    public class Binomial {

        private static readonly double LOG10 = Math.Log(10);

        private readonly int max;
        private readonly PrimeSieve ps;

        private readonly BigInteger[,] binomialLookup;
        private readonly int lookupLimit;

        public Binomial(int max, int lookup) {

            this.max = max;

            ps = new PrimeSieve(this.max);

            if (lookup < 10) {
                lookup = 10;
            }
            this.lookupLimit = lookup;

            int lookup2 = lookup / 2;

            binomialLookup = new BigInteger[lookup + 1, lookup2 + 1];

            for (int total = 1; total <= lookup; total++) {
                for (int choose = 0; choose <= total / 2; choose++) {
                    //try {
                    binomialLookup[total, choose] = Generate(choose, total);
                    //Console.WriteLine("Binomial " + total + " choose " + choose + " is " + binomialLookup[total, choose]);
                    //} catch (Exception e) {
                    //    throw e;
                    //}
                }
            }
        }


        public BigInteger Generate(int k, int n) {

            if (n == 0 && k == 0) {
                return BigInteger.One;
            }

            if (n < 1) {
                throw new Exception("Binomial: 1 <= n required, but n was " + n);
            }

            if (0 > k || k > n) {
                throw new Exception("Binomial: 0 <= k and k <= n required, but n was " + n + " and k was " + k);
            }

            int choose = Math.Min(k, n - k);

            if (n <= lookupLimit && binomialLookup[n, choose] != 0) {  // it is zero when it hasn't been built yet
                return binomialLookup[n, choose];
            } else if (choose < 125) {
                return Combination(choose, n);
            } else if (n <= max) {
                return CombinationLarge(choose, n);
            } else {
                return CombinationApprox(choose, n);
            }

        }

        private static BigInteger Combination(int mines, int squares) {

            BigInteger top = BigInteger.One;
            BigInteger bot = BigInteger.One;

            int range = Math.Min(mines, squares - mines);

            // calculate the combination. 
            for (int i = 0; i < range; i++) {
                top = top * new BigInteger(squares - i);
                bot = bot * new BigInteger(i + 1);
            }

            BigInteger result = top / bot;

            return result;

        }


        private BigInteger CombinationLarge(int k, int n) {

            if ((k == 0) || (k == n)) return BigInteger.One;

            int n2 = n / 2;

            if (k > n2) {
                k = n - k;
            }

            int nk = n - k;

            int rootN = (int)Math.Floor(Math.Sqrt(n));

            BigInteger result = BigInteger.One;

            foreach (int prime in ps.getPrimesIterable(2, n)) {

                if (prime > nk) {
                    result = result * new BigInteger(prime);
                    continue;
                }

                if (prime > n2) {
                    continue;
                }

                if (prime > rootN) {
                    if (n % prime < k % prime) {
                        result = result * new BigInteger(prime);
                    }
                    continue;
                }

                int r = 0, N = n, K = k, p = 1;

                while (N > 0) {
                    r = (N % prime) < (K % prime + r) ? 1 : 0;
                    if (r == 1) {
                        p *= prime;
                    }
                    N /= prime;
                    K /= prime;
                }
                if (p > 1) {
                    result = result * new BigInteger(p);
                }
            }

            return result;
        }

        // use the stirling approximation for factorials to create an approximate combinatorial
        public BigInteger CombinationApprox(int k, int n) {

            double logComb = (LogFactorialApprox(n) - LogFactorialApprox(k) - LogFactorialApprox(n - k));

            int power = (int) Math.Floor(logComb);

            int dp = Math.Min(6, power);

            //double value = Math.Exp((logComb - power + dp) * LOG10);
            //BigInteger result = new BigInteger(sigDigit) * BigInteger.Pow(new BigInteger(10), power - dp);

            // find the significant digits
            int sigDigits = (int) Math.Round(Math.Pow(10, logComb - power + dp));

            String scientific = "" + sigDigits + "E" + (power - dp);

            BigInteger result = BigInteger.Parse(scientific, NumberStyles.AllowExponent);

            return result;

        }


        // returns an approximation for Log(n!)
        private double LogFactorialApprox(int n) {
            return n * Math.Log10(n) + 0.5d * Math.Log10(2 * Math.PI * n);
        }
    }
}
