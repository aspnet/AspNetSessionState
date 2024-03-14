namespace Microsoft.AspNet.SessionState
{
    internal static class StringExtensions
    {
        //============================================================================ robs ==
        // This is to replace the call to GetHashCode() when appending AppId to the session id.
        // Using GetHashCode() is not deterministic and can cause problems when used externally (e.g in SQL)
        // Credit: https://andrewlock.net/why-is-string-gethashcode-different-each-time-i-run-my-program-in-net-core/
        //====================================================================== 2023-07-21 ==
        internal static int GetDeterministicHashCode(this string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }
    }
}
