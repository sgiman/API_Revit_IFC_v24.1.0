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
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Import.Enums;
using Revit.IFC.Import.Utility;

namespace Revit.IFC.Import.Data
{
   /// <summary>
   /// Represents an IfcBuilding.
   /// </summary>
   public class IFCBuilding : IFCSpatialStructureElement
   {
      /// <summary>
      /// Constructs an IFCBuilding from the IfcBuilding handle.
      /// </summary>
      /// <param name="ifcBuilding">The IfcBuilding handle.</param>
      protected IFCBuilding(IFCAnyHandle ifcBuilding)
      {
         Process(ifcBuilding);
      }

      /// <summary>
      /// The base elevation of the building.
      /// </summary>
      public double ElevationOfRefHeight { get; protected set; } = 0.0;

      /// <summary>
      /// The elevation above the minimal terrain level.
      /// </summary>
      public double ElevationOfTerrain { get; protected set; } = 0.0;

      /// <summary>
      /// The optional address given to the building for postal purposes.
      /// </summary>
      public IFCPostalAddress BuildingAddress { get; protected set; } = null;

      /// <summary>
      /// Processes IfcBuilding attributes.
      /// </summary>
      /// <param name="ifcBuilding">The IfcBuilding handle.</param>
      protected override void Process(IFCAnyHandle ifcBuilding)
      {
         base.Process(ifcBuilding);

         ElevationOfRefHeight = IFCImportHandleUtil.GetOptionalScaledLengthAttribute(ifcBuilding, "ElevationOfRefHeight", 0.0);

         ElevationOfTerrain = IFCImportHandleUtil.GetOptionalScaledLengthAttribute(ifcBuilding, "ElevationOfTerrain", 0.0);

         IFCAnyHandle ifcPostalAddress = IFCImportHandleUtil.GetOptionalInstanceAttribute(ifcBuilding, "BuildingAddress");
         if (!IFCAnyHandleUtil.IsNullOrHasNoValue(ifcPostalAddress))
            BuildingAddress = IFCPostalAddress.ProcessIFCPostalAddress(ifcPostalAddress);
      }

      public override void PostProcess()
      {
         TryToFixFarawayOrigin();
         base.PostProcess();
      }


      /// <summary>
      /// Allow for override of IfcObjectDefinition shared parameter names.
      /// </summary>
      /// <param name="name">The enum corresponding of the shared parameter.</param>
      /// <param name="isType">True if the shared parameter is a type parameter.</param>
      /// <returns>The name appropriate for this IfcObjectDefinition.</returns>
      public override string GetSharedParameterName(IFCSharedParameters name, bool isType)
      {
         if (!isType)
         {
            switch (name)
            {
               case IFCSharedParameters.IfcName:
                  return "BuildingName";
               case IFCSharedParameters.IfcDescription:
                  return "BuildingDescription";
            }
         }

         return base.GetSharedParameterName(name, isType);
      }

      /// <summary>
      /// Get the element ids created for this entity, for summary logging.
      /// </summary>
      /// <param name="createdElementIds">The creation list.</param>
      /// <remarks>May contain InvalidElementId; the caller is expected to remove it.</remarks>
      public override void GetCreatedElementIds(ISet<ElementId> createdElementIds)
      {
         // If we used ProjectInformation, don't report that.
         if (CreatedElementId != ElementId.InvalidElementId && CreatedElementId != Importer.TheCache.ProjectInformationId)
         {
            createdElementIds.Add(CreatedElementId);
         }
      }

      /// <summary>
      /// Creates or populates Revit elements based on the information contained in this class.
      /// </summary>
      /// <param name="doc">The document.</param>
      protected override void Create(Document doc)
      {
         base.Create(doc);

         IFCLocation.WarnIfFaraway(this);

         // IfcBuilding usually won't create an element, as it contains no geometry.
         // If it doesn't, use the ProjectInfo element in the document to store its parameters.
         if (CreatedElementId == ElementId.InvalidElementId)
            CreatedElementId = Importer.TheCache.ProjectInformationId;
      }

      /// <summary>
      /// Creates or populates Revit element params based on the information contained in this class.
      /// </summary>
      /// <param name="doc">The document.</param>
      /// <param name="element">The element.</param>
      protected override void CreateParametersInternal(Document doc, Element element)
      {
         base.CreateParametersInternal(doc, element);

         CreatePostalParameters(doc, element, BuildingAddress);
      }

      /// <summary>
      /// Processes an IfcBuilding object.
      /// </summary>
      /// <param name="ifcBuilding">The IfcBuilding handle.</param>
      /// <returns>The IFCBuilding object.</returns>
      public static IFCBuilding ProcessIFCBuilding(IFCAnyHandle ifcBuilding)
      {
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcBuilding))
         {
            Importer.TheLog.LogNullError(IFCEntityType.IfcBuilding);
            return null;
         }

         IFCEntity building;
         if (!IFCImportFile.TheFile.EntityMap.TryGetValue(ifcBuilding.StepId, out building))
            building = new IFCBuilding(ifcBuilding);
         return (building as IFCBuilding);
      }
   }
}