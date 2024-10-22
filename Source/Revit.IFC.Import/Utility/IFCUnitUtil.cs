﻿/*******************************************************************************
 * ........
 * 
 * Autodesk Revit 24.0.4.427 (ENU) - IFC 24.1.1.6 (IFC import/Export) 
 * https://github.com/Autodesk/revit-ifc/releases
 *
 * -----------------------------------------------------------------------------
 * Create Build (API REVIT 2024) 
 * Application (add-ins)
 * -----------------------------------------------------------------------------
 * Visual Studio 2022 
 * C# | .NET 4.8
 * ----------------------------------------------------------------------------- 
 * Writing sgiman @ 2023 
 *******************************************************************************/
//
// Revit IFC Import library: this library works with Autodesk(R) Revit(R) to import IFC files.
// Copyright (C) 2013  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit.IFC.Common.Utility;
using Revit.IFC.Import.Data;

namespace Revit.IFC.Import.Utility
{
   /// <summary>
   /// Provides methods to scale IFC units.
   /// </summary>
   public class IFCUnitUtil
   {
      /// <summary>
      /// Converts an angle value from the units from an IFC file to the corresponding Revit internal units.
      /// </summary>
      /// <param name="inValue">The value to convert.</param>
      /// <returns>The result value in Revit internal units.</returns>
      static public double ScaleAngle(double inValue)
      {
         return ProjectScale(SpecTypeId.Angle, inValue);
      }

      /// <summary>
      /// Converts a length value from the units from an IFC file to the corresponding Revit internal units.
      /// </summary>
      /// <param name="inValue">The value to convert.</param>
      /// <returns>The result value in Revit internal units.</returns>
      static public double ScaleLength(double inValue)
      {
         return ProjectScale(SpecTypeId.Length, inValue);
      }

      /// <summary>
      /// Converts a vector from the units from an IFC file to the corresponding Revit internal units.
      /// </summary>
      /// <param name="inValue">The value to convert.</param>
      /// <returns>The result value in Revit internal units.</returns>
      /// <remarks>Note that the OffsetFactor is ignored.</remarks>
      static public XYZ ScaleLength(XYZ inValue)
      {
         IFCUnit projectUnit = IFCImportFile.TheFile.IFCUnits.GetIFCProjectUnit(SpecTypeId.Length);
         if (projectUnit != null)
            return inValue * projectUnit.ScaleFactor;

         return inValue;
      }

      /// <summary>
      /// Converts a value from the units from an IFC file to the corresponding Revit internal units.
      /// </summary>
      /// <param name="specTypeId">Identifier of the spec for this value.</param>
      /// <param name="inValue">The value to convert.</param>
      /// Some units we really always want in the standard document units.
      /// For example, angles in Radians.
      /// </param>
      /// <returns>The result value in Revit internal units.</returns>
      static private double ProjectScale(ForgeTypeId specTypeId, double inValue)
      {
         IFCUnit projectUnit = IFCImportFile.TheFile.IFCUnits.GetIFCProjectUnit(specTypeId);
         if (projectUnit != null)
            return inValue * projectUnit.ScaleFactor - projectUnit.OffsetFactor;

         return inValue;
      }

      /// <summary>
      /// Converts a list of vectors from the units from an IFC file to the corresponding Revit internal units.
      /// </summary>
      /// <param name="specTypeId">Identifier of the spec for this value.</param>
      /// <param name="inValue">The value to convert.</param>
      /// <returns>The result value in Revit internal units.</returns>
      /// <remarks>Note that the OffsetFactor is ignored.</remarks>
      static public void ProjectScale(ForgeTypeId specTypeId, IList<XYZ> inValues)
      {
         if (inValues == null)
            return;

         IFCUnit projectUnit = IFCImportFile.TheFile.IFCUnits.GetIFCProjectUnit(specTypeId);
         if (projectUnit == null)
            return;

         double factor = projectUnit.ScaleFactor;
         if (MathUtil.IsAlmostEqual(factor, 1.0))
            return;

         int count = inValues.Count;
         for (int ii = 0; ii < count; ii++)
            inValues[ii] *= factor;
      }

      /// <summary>
      /// Convert a value into a formatted length string as displayed in Revit.
      /// </summary>
      /// <param name="value">The value, in Revit internal units.</param>
      /// <returns>The formatted string representation.</returns>
      static public string FormatLengthAsString(double value)
      {
         FormatValueOptions formatValueOptions = new FormatValueOptions();
         formatValueOptions.AppendUnitSymbol = true;
         FormatOptions lengthFormatOptions = IFCImportFile.TheFile.Document.GetUnits().GetFormatOptions(SpecTypeId.Length);
         lengthFormatOptions.Accuracy = 1e-8;
         if (lengthFormatOptions.CanSuppressTrailingZeros())
            lengthFormatOptions.SuppressTrailingZeros = true;
         formatValueOptions.SetFormatOptions(lengthFormatOptions);
         return UnitFormatUtils.Format(IFCImportFile.TheFile.Document.GetUnits(), SpecTypeId.Length, value, false, formatValueOptions);
      }
   }
}