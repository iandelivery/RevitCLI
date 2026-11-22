using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitCliBridge
{
    internal static class ConversionUtilities
    {
        public static string NumberToChinese(this int num)
        {
            string[] chineseDigits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            string[] chineseUnits = { "", "十", "百", "千" };

            string result = "";
            int index = 0;

            while (num > 0)
            {
                int digit = num % 10;
                if (digit != 0 || index > 0)
                {
                    result = chineseDigits[digit] + chineseUnits[index] + result;
                }
                num /= 10;
                index++;
            }

            return result;
        }

        /// <summary>
        /// Feet to millimeters. Output-side conversion: rounds to the
        /// units-contract precision (three decimals) through
        /// <see cref="UnitContracts"/>, which kills conversion noise such as
        /// 832.4999999998686. The factors are exact definitions, so no Revit
        /// version branch is needed.
        /// </summary>
        public static double FeetToMillimeter(this double feet)
        {
            return UnitContracts.FromInternal(UnitKinds.Length, "mm", feet);
        }

        /// <summary>
        /// Millimeters to feet. Input-side conversion: never rounds.
        /// </summary>
        public static double MillimeterToFeet(this double millimeter)
        {
            return UnitContracts.ToInternal(UnitKinds.Length, "mm", millimeter);
        }

        /// <summary>
        /// Square feet to square meters, rounded to contract precision.
        /// </summary>
        public static double SquareFeetToSquareMeters(this double squareFeet)
        {
            return UnitContracts.FromInternal(UnitKinds.Area, "m2", squareFeet);
        }

        /// <summary>
        /// Radians to degrees, rounded to contract precision.
        /// </summary>
        public static double RadiansToDegrees(this double radians)
        {
            return UnitContracts.FromInternal(UnitKinds.Angle, "deg", radians);
        }

        /// <summary>
        /// Degrees to radians. Input-side conversion: never rounds.
        /// </summary>
        public static double DegreesToRadians(this double degrees)
        {
            return UnitContracts.ToInternal(UnitKinds.Angle, "deg", degrees);
        }
    }
}
