﻿/*******************************************************************************
 * ElectricalCurrentPropertyUtil.cs
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
// BIM IFC library: this library works with Autodesk(R) Revit(R)
// to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Utility;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Common.Utility;

namespace Revit.IFC.Export.Exporter.PropertySet
{
   /// <summary>
   /// Provides static methods to create varies IFC properties.
   /// </summary>
   public class ElectricalCurrentPropertyUtil : PropertyUtil
   {
      /// <summary>
      /// Create a label property.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="propertyName">The name of the property.</param>
      /// <param name="value">The value of the property.</param>
      /// <param name="valueType">The value type of the property.</param>
      /// <returns>The created property handle.</returns>
      public static IFCAnyHandle CreateElectricalCurrentMeasureProperty(IFCFile file, string propertyName, double value, PropertyValueType valueType)
      {
         switch (valueType)
         {
            case PropertyValueType.EnumeratedValue:
               {
                  IList<IFCData> valueList = new List<IFCData>();
                  valueList.Add(IFCDataUtil.CreateAsElectricCurrentMeasure(value));
                  return IFCInstanceExporter.CreatePropertyEnumeratedValue(file, propertyName, null, valueList, null);
               }
            case PropertyValueType.SingleValue:
               return IFCInstanceExporter.CreatePropertySingleValue(file, propertyName, null, IFCDataUtil.CreateAsElectricCurrentMeasure(value), null);
            default:
               throw new InvalidOperationException("Missing case!");
         }
      }

      /// <summary>
      /// Create a label property, or retrieve from cache.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="propertyName">The name of the property.</param>
      /// <param name="value">The value of the property.</param>
      /// <param name="valueType">The value type of the property.</param>
      /// <returns>The created or cached property handle.</returns>
      public static IFCAnyHandle CreateElectricalCurrentMeasurePropertyFromCache(IFCFile file, string propertyName, double value, PropertyValueType valueType)
      {
         // We have a partial cache here - we will only cache multiples of 15 degrees.
         bool canCache = false;
         double ampsDiv5 = Math.Floor(value / 5.0 + 0.5);
         double integerAmps = ampsDiv5 * 5.0;
         if (MathUtil.IsAlmostEqual(value, integerAmps))
         {
            canCache = true;
            value = integerAmps;
         }

         IFCAnyHandle propertyHandle;
         if (canCache)
         {
            propertyHandle = ExporterCacheManager.PropertyInfoCache.ElectricCurrentCache.Find(propertyName, value);
            if (propertyHandle != null)
               return propertyHandle;
         }

         propertyHandle = CreateElectricalCurrentMeasureProperty(file, propertyName, value, valueType);

         if (canCache && !IFCAnyHandleUtil.IsNullOrHasNoValue(propertyHandle))
         {
            ExporterCacheManager.PropertyInfoCache.ElectricCurrentCache.Add(propertyName, value, propertyHandle);
         }

         return propertyHandle;
      }

      /// <summary>
      /// Create an electrical current measure property from the element's or type's parameter.
      /// </summary>
      /// <param name="file">The IFC file.</param>
      /// <param name="elem">The Element.</param>
      /// <param name="revitParameterName">The name of the parameter.</param>
      /// <param name="ifcPropertyName">The name of the property.</param>
      /// <param name="valueType">The value type of the property.</param>
      /// <returns>The created property handle.</returns>
      public static IFCAnyHandle CreateElectricalCurrentMeasurePropertyFromElement(IFCFile file, Element elem, string revitParameterName, string ifcPropertyName, PropertyValueType valueType)
      {
         double propertyValue;
         if (ParameterUtil.GetDoubleValueFromElement(elem, revitParameterName, out propertyValue) != null)
         {
            propertyValue = UnitUtil.ScaleElectricCurrent(propertyValue);
            return CreateElectricalCurrentMeasurePropertyFromCache(file, ifcPropertyName, propertyValue, valueType);
         }

         return null;
      }
   }
}
