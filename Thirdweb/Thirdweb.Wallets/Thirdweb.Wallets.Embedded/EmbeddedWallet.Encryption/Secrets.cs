using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Thirdweb.EWS
{
    internal class Secrets
    {
        private Config config = new(Defaults.nBits);
        private const int nHexDigitBits = 4;
        private readonly Func<int, int> GetRandomInt32 = (nBits) => RandomNumberGenerator.GetInt32(1, 1 << nBits);
        private static readonly string padding = string.Join("", Enumerable.Repeat("0", Defaults.maxPaddingMultiple));
        private static readonly string[] nybbles = { "0000", "0001", "0010", "0011", "0100", "0101", "0110", "0111", "1000", "1001", "1010", "1011", "1100", "1101", "1110", "1111", };

        /// <summary>
        /// Reconsitute a secret from <paramref name="shares"/>.
        /// </summary>
        /// <remarks>
        /// <para>The return value will <c>not</c> be the original secret if the number of shares provided is less than the threshold
        /// number of shares.</para>
        /// <para>Duplicate shares do not count toward the threshold.</para>
        /// </remarks>
        /// <param name="shares">The shares used to reconstitute the secret.</param>
        /// <returns>The reconstituted secret.</returns>
        public string Combine(IReadOnlyList<string> shares)
        {
            return Combine(shares, 0);
        }

        /// <summary>
        /// Convert a string of hexadecimal digits into a byte array.
        /// </summary>
        /// <param name="s">The string of hexadecimal digits to convert.</param>
        /// <returns>A byte array.</returns>
        public static byte[] GetBytes(string s)
        {
            byte[] bytes = Enumerable.Range(0, s.Length / 2).Select((i) => byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber)).ToArray();
            return bytes;
        }

        /// <summary>
        /// Convert a byte array into a string of hexadecimal digits.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <returns>A string of hexadecimal digits.</returns>
        public static string GetHexString(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Generate a new share identified as <paramref name="shareId"/>.
        /// </summary>
        /// <remarks>
        /// <para>The return value will be invalid if the number of shares provided is less than the threshold number of shares.</para>
        /// <para>If <paramref name="shareId"/> is the identifier of a share in <paramref name="shares"/> and the number of shares
        /// provided is at least the threshold number of shares, the return value will be the same as the identified share.</para>
        /// <para>Duplicate shares do not count toward the threshold.</para>
        /// </remarks>
        /// <param name="shareId">The identifier of the share to generate.</param>
        /// <param name="shares">The shares from which to generate the new share.</param>
        /// <returns>A hexadecimal string of the new share.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public string NewShare(int shareId, IReadOnlyList<string> shares)
        {
            if (shareId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shareId), $"{nameof(shareId)} must be greater than zero.");
            }
            else if (shares == null || !shares.Any() || string.IsNullOrEmpty(shares[0]))
            {
                throw new ArgumentException($"{nameof(shares)} cannot be empty.", nameof(shares));
            }
            ShareComponents share = ExtractShareComponents(shares[0]);
            return ConstructPublicShareString(share.nBits, Convert.ToString(shareId, Defaults.radix), Combine(shares, shareId));
        }

        /// <summary>
        /// Generate a random value expressed as a string of hexadecimal digits that contains <paramref name="nBytes"/> bytes using a
        /// secure random number generator.
        /// </summary>
        /// <param name="nBytes">The number of bytes of output.</param>
        /// <returns>A hexadecimal string of the value.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string Random(int nBytes)
        {
            const int maxnBytes = (1 << 16) / 8;
            if (nBytes < 1 || nBytes > maxnBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(nBytes), $"{nameof(nBytes)} must be in the range [1, {maxnBytes}].");
            }
            var bytes = new byte[nBytes];
            RandomNumberGenerator.Fill(bytes);
            string rv = GetHexString(bytes);
            return rv;
        }

        /// <summary>
        /// Divide a <paramref name="secret"/> into <paramref name="nShares"/>
        /// shares, requiring <paramref name="threshold"/> shares to
        /// reconstruct the secret.  Optionally, initialize with <paramref name="nBits"/>. Optionally, zero-pad the secret to a length
        /// that is a multiple of <paramref name="paddingMultiple"/> (default 128) before sharing.
        /// </summary>
        /// <param name="secret">A secret value expressed as a string of hexadecimal digits.</param>
        /// <param name="nShares">The number of shares to produce.</param>
        /// <param name="threshold">The number of shares required to reconstruct the secret.</param>
        /// <param name="nBits">The number of bits to use to create the shares.</param>
        /// <param name="paddingMultiple">The amount of zero-padding to apply to the secret before sharing.</param>
        /// <returns>A list of strings of hexadecimal digits.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public List<string> Share(string secret, int nShares, int threshold, int nBits = 0, int paddingMultiple = 128)
        {
            // Initialize based on nBits if it's specified.
            if (nBits != 0)
            {
                if (nBits < Defaults.minnBits || nBits > Defaults.maxnBits)
                {
                    throw new ArgumentOutOfRangeException(nameof(nBits), $"{nameof(nBits)} must be in the range [{Defaults.minnBits}, {Defaults.maxnBits}].");
                }
                config = new(nBits);
            }

            // Validate the parameters.
            if (string.IsNullOrEmpty(secret))
            {
                throw new ArgumentException($"{nameof(secret)} cannot be empty.", nameof(secret));
            }
            else if (!secret.All((ch) => char.IsDigit(ch) || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f')))
            {
                throw new ArgumentException($"{nameof(secret)} must consist only of hexadecimal digits.", nameof(secret));
            }
            else if (nShares < 2 || nShares > Math.Min(config.maxnShares, Defaults.maxnShares))
            {
                if (nShares > Defaults.maxnShares)
                {
                    throw new ArgumentOutOfRangeException(nameof(nShares), $"The maximum number of shares is {Defaults.maxnShares} since the maximum bit count is {Defaults.maxnBits}.");
                }
                else if (nShares > config.maxnShares)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(nShares),
                        $"{nameof(nShares)} must be in the range [2, {config.maxnShares}]. To create {nShares} shares, specify at least {Math.Ceiling(Math.Log(nShares + 1, 2))} bits."
                    );
                }
                throw new ArgumentOutOfRangeException(nameof(nShares), $"{nameof(nShares)} must be in the range [2, {config.maxnShares}].");
            }
            else if (threshold < 2 || threshold > nShares)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold), $"{nameof(threshold)} must be in the range [2, {nShares}].");
            }
            else if (paddingMultiple < 0 || paddingMultiple > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(paddingMultiple), $"{nameof(paddingMultiple)} must be in the range [0, {Defaults.maxPaddingMultiple}].");
            }

            // Prepend a 1 as a marker to preserve the correct number of leading zeros in the secret.
            secret = "1" + Hex2bin(secret);

            // Create the shares.  For additional security, pad in multiples of 128 bits by default.  This is a small trade-off in larger
            // share size to help prevent leakage of information about small secrets and increase the difficulty of attacking them.
            List<int> l = SplitNumStringToIntArray(secret, paddingMultiple);
            var x = new string[nShares];
            var y = new string[nShares];
            foreach (int value in l)
            {
                var subShares = GetShares(value, nShares, threshold);
                for (int i = 0; i < nShares; ++i)
                {
                    x[i] = Convert.ToString(subShares[i].x, Defaults.radix);
                    y[i] = PadLeft(Convert.ToString(subShares[i].y, 2), config.nBits) + (y[i] ?? "");
                }
            }
            for (int i = 0; i < nShares; ++i)
            {
                x[i] = ConstructPublicShareString(config.nBits, x[i], Bin2hex(y[i]));
            }
            return x.ToList();
        }

        private static string Bin2hex(string value)
        {
            value = PadLeft(value, nHexDigitBits);
            StringBuilder sb = new();
            for (int i = 0; i < value.Length; i += nHexDigitBits)
            {
                int num = Convert.ToInt32(value.Substring(i, nHexDigitBits), 2);
                sb.Append(Convert.ToString(num, 16));
            }
            return sb.ToString();
        }

        private string Combine(IReadOnlyList<string> shares, int shareId)
        {
            // Zip distinct shares.  E.g.
            // [ [ 193, 186, 29, 177, 196 ],
            //   [ 53, 105, 139, 127, 149 ],
            //   [ 146, 211, 249, 206, 81 ] ]
            // becomes
            // [ [ 193, 53, 146 ],
            //   [ 186, 105, 211 ],
            //   [ 29, 139, 249 ],
            //   [ 177, 127, 206 ],
            //   [ 196, 149, 81 ] ]
            int nBits = 0;
            List<int> x = new();
            List<List<int>> y = new();
            foreach (ShareComponents share in shares.Select((s) => ExtractShareComponents(s)))
            {
                // All shares must have the same bits settings.
                if (nBits == 0)
                {
                    nBits = share.nBits;

                    // Reconfigure based on the bits settings of the shares.
                    if (config.nBits != nBits)
                    {
                        config = new(nBits);
                    }
                }
                else if (share.nBits != nBits)
                {
                    throw new ArgumentException("Shares are mismatched due to different bits settings.", nameof(shares));
                }

                // Spread the share across the arrays if the share.id is not already in array `x`.
                if (x.IndexOf(share.id) == -1)
                {
                    x.Add(share.id);
                    List<int> splitShare = SplitNumStringToIntArray(Hex2bin(share.data));
                    for (int i = 0, n = splitShare.Count; i < n; ++i)
                    {
                        if (i >= y.Count)
                        {
                            y.Add(new List<int>());
                        }
                        y[i].Add(splitShare[i]);
                    }
                }
            }

            // Extract the secret from the zipped share data.
            StringBuilder sb = new();
            foreach (List<int> y_ in y)
            {
                sb.Insert(0, PadLeft(Convert.ToString(Lagrange(shareId, x, y_), 2), nBits));
            }
            string result = sb.ToString();

            // If `shareId` is not zero, NewShare invoked Combine.  In this case, return the new share data directly.  Otherwise, find
            // the first '1' which was added in the Share method as a padding marker and return only the data after the padding and the
            // marker.  Convert the binary string, which is the derived secret, to hexadecimal.
            return Bin2hex(shareId >= 1 ? result : result[(result.IndexOf("1") + 1)..]);
        }

        private static string ConstructPublicShareString(int nBits, string shareId, string data)
        {
            int id = Convert.ToInt32(shareId, Defaults.radix);
            string base36Bits = char.ConvertFromUtf32(nBits > 9 ? nBits - 10 + 'A' : nBits + '0');
            int idMax = (1 << nBits) - 1;
            int paddingMultiple = Convert.ToString(idMax, Defaults.radix).Length;
            string hexId = PadLeft(Convert.ToString(id, Defaults.radix), paddingMultiple);
            if (id < 1 || id > idMax)
            {
                throw new ArgumentOutOfRangeException(nameof(shareId), $"{nameof(shareId)} must be in the range [1, {idMax}].");
            }
            string share = base36Bits + hexId + data;
            return share;
        }

        private static ShareComponents ExtractShareComponents(string share)
        {
            // Extract the first character which represents the number of bits in base 36.
            int nBits = GetLargeBaseValue(share[0]);
            if (nBits < Defaults.minnBits || nBits > Defaults.maxnBits)
            {
                throw new ArgumentException($"Unexpected {nBits}-bit share outside of the range [{Defaults.minnBits}, {Defaults.maxnBits}].", nameof(share));
            }

            // Calculate the maximum number of shares allowed for the given number of bits.
            int maxnShares = (1 << nBits) - 1;

            // Derive the identifier length from the bit count.
            int idLength = Convert.ToString(maxnShares, Defaults.radix).Length;

            // Extract all the parts now that the segment sizes are known.
            var rx = new Regex("^([3-9A-Ka-k]{1})([0-9A-Fa-f]{" + idLength + "})([0-9A-Fa-f]+)$");
            MatchCollection shareComponents = rx.Matches(share);
            GroupCollection groups = shareComponents.FirstOrDefault()?.Groups;
            if (groups == null || groups.Count != 4)
            {
                throw new ArgumentException("Malformed share", nameof(share));
            }

            // Convert the identifier from a string of hexadecimal digits into an integer.
            int id = Convert.ToInt32(groups[2].Value, Defaults.radix);

            // Return the components of the share.
            ShareComponents rv = new(nBits, id, groups[3].Value);
            return rv;
        }

        private static int GetLargeBaseValue(char ch)
        {
            int rv =
                ch >= 'a'
                    ? ch - 'a' + 10
                    : ch >= 'A'
                        ? ch - 'A' + 10
                        : ch - '0';
            return rv;
        }

        private (int x, int y)[] GetShares(int secret, int nShares, int threshold)
        {
            int[] coefficients = Enumerable.Range(0, threshold - 1).Select((i) => GetRandomInt32(config.nBits)).Concat(new[] { secret }).ToArray();
            var shares = Enumerable.Range(1, nShares).Select((i) => (i, Horner(i, coefficients))).ToArray();
            return shares;
        }

        private static string Hex2bin(string value)
        {
            StringBuilder sb = new();
            foreach (char ch in value)
            {
                sb.Append(nybbles[GetLargeBaseValue(ch)]);
            }
            return sb.ToString();
        }

        // Evaluate the polynomial at `x` using Horner's Method.
        // NOTE: fx = fx * x + coefficients[i] -> exp(log(fx) + log(x)) + coefficients[i], so if fx is zero, set fx to coefficients[i]
        // since using the exponential or logarithmic form will result in an incorrect value.
        private int Horner(int x, IEnumerable<int> coefficients)
        {
            int logx = config.logarithms[x];
            int fx = 0;
            foreach (int coefficient in coefficients)
            {
                fx = fx == 0 ? coefficient : config.exponents[(logx + config.logarithms[fx]) % config.maxnShares] ^ coefficient;
            }
            return fx;
        }

        // Evaluate the Lagrange interpolation polynomial at x = `shareId` using x and y arrays that are of the same length, with
        // corresponding elements constituting points on the polynomial.
        private int Lagrange(int shareId, IReadOnlyList<int> x, IReadOnlyList<int> y)
        {
            int sum = 0;
            foreach (int i in Enumerable.Range(0, x.Count))
            {
                if (i < y.Count && y[i] != 0)
                {
                    int product = config.logarithms[y[i]];
                    foreach (int j in Enumerable.Range(0, x.Count).Where((j) => i != j))
                    {
                        if (shareId == x[j])
                        {
                            // This happens when computing a share that is in the list of shares used to compute it.
                            product = -1;
                            break;
                        }

                        // Ensure it's not negative.
                        product = (product + config.logarithms[shareId ^ x[j]] - config.logarithms[x[i] ^ x[j]] + config.maxnShares) % config.maxnShares;
                    }
                    sum = product == -1 ? sum : sum ^ config.exponents[product];
                }
            }
            return sum;
        }

        private static string PadLeft(string value, int paddingMultiple)
        {
            if (paddingMultiple == 1)
            {
                return value;
            }
            else if (paddingMultiple < 2 || paddingMultiple > Defaults.maxPaddingMultiple)
            {
                throw new ArgumentOutOfRangeException(nameof(paddingMultiple), $"{nameof(paddingMultiple)} must be in the range [0, {Defaults.maxPaddingMultiple}].");
            }
            if (value.Any())
            {
                int extra = value.Length % paddingMultiple;
                if (extra > 0)
                {
                    string s = padding + value;
                    value = s[^(paddingMultiple - extra + value.Length)..];
                }
            }
            return value;
        }

        private List<int> SplitNumStringToIntArray(string value, int paddingMultiple = 0)
        {
            if (paddingMultiple > 0)
            {
                value = PadLeft(value, paddingMultiple);
            }
            List<int> parts = new();
            int i;
            for (i = value.Length; i > config.nBits; i -= config.nBits)
            {
                parts.Add(Convert.ToInt32(value.Substring(i - config.nBits, config.nBits), 2));
            }
            parts.Add(Convert.ToInt32(value[..i], 2));
            return parts;
        }

        private class Config
        {
            internal readonly int[] exponents;
            internal readonly int[] logarithms;
            internal readonly int maxnShares;
            internal readonly int nBits;

            internal Config(int nBits)
            {
                // Set the scalar values.
                this.nBits = nBits;
                int size = 1 << nBits;
                maxnShares = size - 1;

                // Construct the exponent and logarithm tables for multiplication.
                int primitive = Defaults.primitivePolynomialCoefficients[nBits];
                exponents = new int[size];
                logarithms = new int[size];
                for (int x = 1, i = 0; i < size; ++i)
                {
                    exponents[i] = x;
                    logarithms[x] = i;
                    x <<= 1;
                    if (x >= size)
                    {
                        x ^= primitive;
                        x &= maxnShares;
                    }
                }
            }
        }

        private class Defaults
        {
            internal const int minnBits = 3;
            internal const int maxnBits = 20; // up to 1,048,575 shares
            internal const int maxnShares = (1 << maxnBits) - 1;
            internal const int maxPaddingMultiple = 1024;
            internal const int nBits = 8;
            internal const int radix = 16; // hexadecimal

            // These are primitive polynomial coefficients for Galois Fields GF(2^n) for 2 <= n <= 20.  The index of each term in the
            // array corresponds to the n for that polynomial.
            internal static readonly int[] primitivePolynomialCoefficients = { -1, -1, 1, 3, 3, 5, 3, 3, 29, 17, 9, 5, 83, 27, 43, 3, 45, 9, 39, 39, 9, };
        }

        private class ShareComponents
        {
            internal int nBits;
            internal int id;
            internal string data;

            internal ShareComponents(int nBits, int id, string data)
            {
                this.nBits = nBits;
                this.id = id;
                this.data = data;
            }
        }
    }
}
