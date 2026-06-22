using Autodesk.Revit.DB;
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

        public static double FeetToMillimeter(this double feet)
        {
#if (R19 || R20)
            return UnitUtils.Convert(feet, DisplayUnitType.DUT_DECIMAL_FEET, DisplayUnitType.DUT_MILLIMETERS);
#elif (R21 || R22)
            return  UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
#else
            throw new NotImplementedException("Unsupported Revit version");
#endif
        }

        public static double MillimeterToFeet(this double millimeter)
        {
#if (R19 || R20)
            return UnitUtils.Convert(millimeter, DisplayUnitType.DUT_MILLIMETERS, DisplayUnitType.DUT_DECIMAL_FEET);
#elif (R21 || R22)
            return UnitUtils.ConvertToInternalUnits(millimeter, UnitTypeId.Millimeters);
#else
            throw new NotImplementedException("Unsupported Revit version");
#endif
        }

        public static double SquareFeetToSquareMeters(this double millimeter)
        {
#if (R19 || R20)
            return UnitUtils.Convert(millimeter, DisplayUnitType.DUT_SQUARE_FEET, DisplayUnitType.DUT_SQUARE_METERS);
#elif (R21 || R22)
            return UnitUtils.ConvertToInternalUnits(millimeter, UnitTypeId.Millimeters);
#else
            throw new NotImplementedException("Unsupported Revit version");
#endif
        }
    }
}
