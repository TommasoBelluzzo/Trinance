#region Using Directives
using System;
using System.Globalization;
#endregion

namespace TrinanceLib
{
    internal static class Utilities
    {
        #region Methods
        public static String FormatMessage(String message, params Object[] parameters)
        {
            return String.Format(CultureInfo.InvariantCulture, message, parameters);
        }

        public static String GetExceptionMessage(Exception e)
        {
            String message = e?.Message;

            if (String.IsNullOrWhiteSpace(message))
                message = "Invalid exception message.";

            return (Char.ToLower(message[0], CultureInfo.InvariantCulture) + message.Substring(1).TrimEnd('.'));
        }
        #endregion
    }
}
