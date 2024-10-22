﻿/*******************************************************************************
 * CommonEnum.cs
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Revit.IFC.Common.Enums
{
   /// <summary>
   /// Enumeration for the basis of WCS reference coordinates
   /// </summary>
   public enum SiteTransformBasis
   {
      Shared = 0,
      Site = 1,
      Project = 2,
      Internal = 3,
      ProjectInTN = 4,
      InternalInTN = 5
   }

   public enum IFCRepresentationIdentifier
   {
      Annotation,
      Axis,
      Body,
      BodyFallback, // Body-Fallback.
      Box,
      Clearance,
      CoG,  // Center of gravity
      FootPrint,
      Lighting,
      Profile,
      Reference,
      Style,
      Surface,
      Other,

      None  // The top level value.
   }
}
