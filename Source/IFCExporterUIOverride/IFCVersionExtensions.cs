﻿/*******************************************************************************
 * IFCVersionExtensions.cs
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
 ********************************************************************************/ 
//
// BIM IFC export alternate UI library: this library works with Autodesk(R) Revit(R)
// to provide an alternate user interface for the export of IFC files from Revit.
// Copyright (C) 2016  Autodesk, Inc.
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

using Autodesk.Revit.DB;
using BIM.IFC.Export.UI.Properties;

namespace BIM.IFC.Export.UI
{
   internal static class IFCVersionExtensions
   {
      /// <summary>
      /// Converts the IFCVersion to string.
      /// </summary>
      /// <returns>The string of IFCVersion.</returns>
      public static string ToLabel(this IFCVersion version)
      {
         switch (version)
         {
            case IFCVersion.IFC2x2:
               return Resources.IFCVersion2x2;
            case IFCVersion.IFC2x3:
               return Resources.IFCVersion2x3;
            case IFCVersion.IFCBCA:
            case IFCVersion.IFC2x3CV2:
               return Resources.IFCMVD2x3CV2;
            case IFCVersion.IFC4:
               return Resources.IFCMVD4CV2;
            case IFCVersion.IFCCOBIE:
               return Resources.IFCMVDGSA;
            case IFCVersion.IFC2x3FM:
               return Resources.IFC2x3FM;
            case IFCVersion.IFC4DTV:
               return Resources.IFC4DTV;
            case IFCVersion.IFC4RV:
               return Resources.IFC4RV;
            case IFCVersion.IFC2x3BFM:
               return Resources.IFCMVDFMHandOver;
            default:
               return Resources.IFCVersionUnrecognized;
         }
      }
   }
}
