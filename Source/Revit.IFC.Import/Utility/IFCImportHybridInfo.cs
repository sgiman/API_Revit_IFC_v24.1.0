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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Import.Data;


namespace Revit.IFC.Import.Utility
{
   /// <summary>
   /// Provide methods to perform Hybrid IFC Import.
   /// </summary>
   public class IFCImportHybridInfo
   {
      /// <summary>
      /// Keeps track of Elements imported (DirectShape/DirectShapeTypes) by AnyCAD
      /// </summary>
      public IList<ElementId> HybridElements { get; set; } = new List<ElementId>();

      /// <summary>
      /// Map from IFCGuid --> Revit ElementId so legacy IFC Processing can find Elements.
      /// </summary>
      public IDictionary<string, ElementId> HybridMap { get; set; } = new Dictionary<string, ElementId>();

      /// <summary>
      /// List of Elements that Import should delete during EndImport.
      /// </summary>
      public IList<ElementId> ElementsToDelete { get; set; } = new List<ElementId>();

      /// <summary>
      /// For IFCProject, Revit will still need to process IFCProductRepresentation/IFCRepresentation/IFCRepresentationItem.
      /// For IFCProjectType, Revit will still need to process IFCRepresentationMap/IFCRepresentation/IFCRepresentationItem.
      /// An example of data that must exist:  LayerAssignment.
      /// In both cases, body geometry will have been created by AnyCAD during pass one, so new body geometry cannot be created.
      /// Communication must be made to IFCRepresentationItem that the IFCProduct/IFCProductType has already had its Representation Created.
      /// That is what this flag indicates:
      /// True:  Representation (Body geometry) already created during pass one.  Ignore all RepresentationItems that might create meshes, etc.  The only
      ///        exception to this is points and curves.  Instead an IFCHybridRepresentationItem will be created as a placeholder.
      /// False:  Representation (Body geometry) should be created like normal with Legacy IFC Import.
      /// </summary>
      public bool RepresentationsAlreadyCreated { get; set; } = false;

      /// <summary>
      /// Document into which IFC Import occurs.
      /// </summary>
      public Document IfcDocument { get; set; } = null;

      /// <summary>
      /// IFC File being imported.
      /// </summary>
      public string IfcInputFile { get; set; } = null;

      /// <summary>
      /// Transform applied to all Elements created via open-source processing within Revit.
      /// </summary>
      public Transform LargeCoordinateTransform { get; set; } = Transform.Identity;

      /// <summary>
      /// Keeps track of one-to-many mapping of entities that will result in container to sub-Element relationships.
      /// Key:  stepId of the container entity.
      /// Value:  Set of IfcObjectDefinition entities that result in sub-Elements.
      /// </summary>
      public IDictionary<int, HashSet<IFCObjectDefinition>> ContainerMap { get; set; } = null;

      /// <summary>
      /// Internal reference to the class that is responsible for doing the actual import and Map creation.
      /// </summary>
      private IFCHybridImport HybridImporter { get; set; } = null;

      public IFCImportHybridInfo(Document ifcDocument, string ifcInputFile)
      {
         HybridImporter = new IFCHybridImport();

         if (ifcDocument == null)
         {
            throw new ArgumentNullException("ifcDocument");
         }

         if (string.IsNullOrWhiteSpace(ifcInputFile))
         {
            throw new ArgumentException("Filename for IFC Input null or empty", ifcInputFile);
         }

         IfcDocument = ifcDocument;
         IfcInputFile = ifcInputFile;

         // Import Elements
         //
         int? elementsImported = ImportElements();
         if (elementsImported == null)
         {
            IfcDocument.Application.WriteJournalComment("Hybrid IFC Import: elementsImportedList = null -- reverting to fallback for entire import.", false);
            Importer.TheLog.LogError(-1, "Hybrid IFC Import:  Unknown Error during Element Import -- aborting", true);
            return;
         }

         if (elementsImported == 0)
         {
            IfcDocument.Application.WriteJournalComment("Hybrid IFC Import: elementsImportedList empty -- reverting to fallback for entire import.", false);
            return;
         }

         // Associate Imported Elements with IFC Guids
         //
         int? associationsPerformed = AssociateElementsWithIFCGuids();
         if (associationsPerformed == null)
         {
            IfcDocument.Application.WriteJournalComment("Hybrid IFC Import: Hybrid IFC Map null -- falling back to Revit for entire import.", false);
            Importer.TheLog.LogError(-1, "Hybrid IFC Import:  Unknown Error during Element / IFC Guid association.", true);
            return;
         }

         // Not an error, but this may hinder the Import Later.
         if (associationsPerformed != elementsImported)
         {
            IfcDocument.Application.WriteJournalComment("Hybrid IFC Import: Count of Elements in map differs from elements Imported -- falling back to Revit for part of import.", false);
            Importer.TheLog.LogWarning(-1, "Hybrid IFC Import:  Number of Element / IFC Guid associations do not match number of imported Elements.", false);
         }

         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import:  Start of Logging detailed Information about AnyCAD Import ---", false);
         Importer.TheLog.LogComment(-1, "Hybrid IFC Import:  If an IfcGuid does not appear in the following list, then it will fallback to Revit processing ---", false);
         LogImportedElementsDetailed();
         LogHybridMapDetailed();
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: End of Logging detailed Information about AnyCAD Import ---", false);
      }

      /// <summary>
      /// Log information about the Hybrid IFC Import Elements Imported.
      /// These ElementIds are imported via AnyCAD, and should be DirectShapes.
      /// This will also log information about the DirectShapeTypes.
      /// </summary>
      /// <remarks>
      /// Because of DirectShapeType logging, all DirectShapes need expansion, which may affect performance.
      /// </remarks>
      public void LogImportedElementsDetailed()
      {
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: Start Imported Element Details. ---", false);
         foreach (ElementId elementId in HybridElements)
         {
            DirectShape shape = IfcDocument?.GetElement(elementId) as DirectShape;
            if (shape == null)
            {
               Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: ElementId Imported, but no Element exists:  {elementId}.", false);
               continue;
            }

            ElementId directShapeType = shape.TypeId;
            if ((directShapeType ?? ElementId.InvalidElementId) == ElementId.InvalidElementId)
            {
               Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: DirectShape Imported with no DirectShapeType: {elementId}.", false);
            }
            else
            {
               Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: (DirectShape, DirectShapeType) Imported: ({elementId}, {directShapeType}).", false);
            }
         }
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: End Imported Element Details. ---", false);
      }

      /// <summary>
      /// Log information about the Hybrid IFC Import Association Map (IFC GlobalId --> Revit ElementId).
      /// </summary>
      public void LogHybridMapDetailed ()
      {
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: Start Hybrid Map Details. ---", false);
         if (HybridMap == null)
         {
            Importer.TheLog.LogComment(-1, "HybridIFCImport:  Hybrid Map not created during import.", false);
         }
         else
         {
            if (HybridMap.Count == 0)
            {
               Importer.TheLog.LogComment(-1, "HybridIFCImport:  Hybrid Map created, but contains no entries.", false);
            }
            else
            {
               foreach (var mapEntry in HybridMap)
               {
                  string ifcGuid = mapEntry.Key;
                  ElementId elementId = mapEntry.Value;
                  if (!string.IsNullOrEmpty(ifcGuid) && ((elementId ?? ElementId.InvalidElementId) != ElementId.InvalidElementId))
                  {
                     Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: Map Entry Created (IFC Guid, ElementId):  ({mapEntry.Key}, {mapEntry.Value})", false);
                     continue;
                  }

                  if (!string.IsNullOrEmpty(ifcGuid))
                  {
                     Importer.TheLog.LogComment(-1, "Hybrid IFC Import:  Hybrid Map entry has no IFC Guid.", false);
                     continue;
                  }

                  if ((elementId ?? ElementId.InvalidElementId) != ElementId.InvalidElementId)
                  {
                     Importer.TheLog.LogComment(-1, $"Hybrid IFC Import:  Hybrid Map entry has no ElementId for {ifcGuid}", false);
                  }
               }
            }
         }
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: End Hybrid Map Details. ---", false);
      }

      /// <summary>
      /// Log ElementIds that will be deleted at the end of Import.  These is populated when Revit must create a new DirectShape for a category change.
      /// </summary>
      public void LogElementsToDeleteDetailed ()
      {
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: Start Elements to be deleted Details. ---", false);
         foreach (ElementId elementId in ElementsToDelete)
         {
            DirectShape shape = IfcDocument?.GetElement(elementId) as DirectShape;
            if (shape == null)
            {
               Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: ElementId identified to be deleted, but no Element exists:  {elementId}.", false);
               continue;
            }

            ElementId directShapeType = shape.TypeId;
            if ((directShapeType ?? ElementId.InvalidElementId) == ElementId.InvalidElementId)
            {
               Importer.TheLog.LogComment(-1, $"Hybrid IFC Import: DirectShape identified to be deleted with no DirectShapeType: {elementId}.", false);
            }
            else
            {
               Importer.TheLog.LogWarning(-1, $"Hybrid IFC Import: (DirectShape, DirectShapeType) indentified to be deleted: ({elementId}, {directShapeType}).", false);
            }
         }
         Importer.TheLog.LogComment(-1, "--- Hybrid IFC Import: End Elements to be deleted Details. ---", false);
      }

      /// <summary>
      /// Import Elements from IFC File using AnyCAD.
      /// Imported Elements will be in the HybridElements data member.
      /// </summary>
      /// <returns>Number of Elements returned.</returns>
      /// <exception cref="InvalidOperationException"></exception>
      public int? ImportElements()
      {
         if (HybridImporter == null)
         {
            throw new InvalidOperationException("Attempting to import elements with null IFCHybridImporter");
         }

         if (IfcDocument == null)
         {
            Importer.TheLog.LogError(-1, "No document for Hybrid IFC Import", true);
            return null;
         }

         if (string.IsNullOrEmpty(IfcInputFile))
         {
            Importer.TheLog.LogError(-1, "Filename for IFC Input null or empty", true);
            return null;
         }

         HybridElements = HybridImporter.ImportElements(IfcDocument, IfcInputFile);

         return HybridElements?.Count;
      }

      /// <summary>
      /// Associate ElementIds with IFCGuids.  In other words, populate the IFCGuid --> ElementId map.
      /// </summary>
      /// <returns>Number of entries in the map.</returns>
      /// <exception cref="InvalidOperationException"></exception>
      public int? AssociateElementsWithIFCGuids()
      {
         if (HybridImporter == null)
         {
            throw new InvalidOperationException("Attempting to associate Elements with IfcGuids with null IFCHybridImporter");
         }

         if (IfcDocument == null)
         {
            Importer.TheLog.LogError(-1, "No document for Hybrid IFC Import", true);
            return null;
         }

         // CreateMap actually returns an ElementId-to-String map.  This is because of two things:
         // 1. We don't know if an external API does a case-sensitive comparison (which is extremely important for IFC GUIDS).
         // 2. We do know that System.String uses a case-sensitive comparison.
         // And then convert.
         IDictionary<IFCGuidKey, ElementId> hybridImportMap = HybridImporter.CreateMap(IfcDocument, HybridElements);
         if (hybridImportMap == null)
         {
            Importer.TheLog.LogError(-1, "Hybrid IFC Import Map set to invalid value.", true);
            return null;
         }

         // IFCGuidKey exists for the sole purpose of retrieving the map using a well-defined operator< in C++.
         foreach (KeyValuePair<IFCGuidKey, ElementId> elementIdGuidPair in hybridImportMap)
         {
            // Use string for IFC Global from here on out.  IFCGuidKey is not generic enough to use as an IFC Global Id elsewhere.
            string ifcGuid = elementIdGuidPair.Key.IFCGlobalId;
            ElementId elementId = elementIdGuidPair.Value;
            if (elementId == ElementId.InvalidElementId)
            {
               Importer.TheLog.LogError(-1, "Invalid Element ID found during Hybrid IFC Import Map construction.", false);
            }
            try
            {
               HybridMap.Add(ifcGuid, elementId);
            }
            catch (ArgumentException ex)
            {
               Importer.TheLog.LogWarning(-1, "Duplicate IFC Global Ids. This will cause some IFC entities to fallback to Revit processing.", false);
               IfcDocument.Application.WriteJournalComment($"Hybrid IFC Import: Duplicate IFC GUIDs detected in Hybrid IFC Map.  Exception message = {ex.Message}", false);
            }
            catch (Exception ex)
            {
               Importer.TheLog.LogWarning(-1, "Error in adding items to IFC GUID-to-ElementId map. This will cause some IFC entities to fallback to Revit processing.", false);
               IfcDocument.Application.WriteJournalComment($"Hybrid IFC Import: Exception adding items to IFC GUID-to-ElementId map.  Exception message = {ex.Message}", false);
            }
         }
         return HybridMap?.Count;
      }

      /// <summary>
      /// Replaces ElementIds in both Hybrid Element list and Hybrid Map (IfcGuid->ElementId)
      /// </summary>
      /// <param name="ifcGuid">GUID of IFC entity.</param>
      /// <param name="oldElementId">Old ElementId to replace.</param>
      /// <param name="newElementId">New ElementId to replace old ElementId with.</param>
      public void ReplaceElementId(string ifcGuid, ElementId oldElementId, ElementId newElementId)
      {
         if ((oldElementId == ElementId.InvalidElementId) || (newElementId == ElementId.InvalidElementId))
            return;

         // Reassign in HybridElements.
         int index = HybridElements.IndexOf(oldElementId);
         if (index == -1)
         {
            Importer.TheLog.LogWarning(-1, $"Unable to replace {ifcGuid} ElementId in list of Hybrid Elements.", true);
            return;
         }

         HybridElements[index] = newElementId;

         // Reassign in HybridMap if it exists.
         if (!HybridMap.ContainsKey(ifcGuid))
         {
            Importer.TheLog.LogWarning(-1, $"Unable to replace {ifcGuid} ElementId in Map of IFCGuids to ElementIds.", true);
            return;
         }
         HybridMap[ifcGuid] = newElementId;
      }

      /// <summary>
      /// Creates a DirectShape simply to contain Geometry copied from other Elements.
      /// The DirectShape won't have any Geometry at this time, but it will be put into the HybridMap.
      /// </summary>
      /// <param name="ifcProduct">IfcProduct entity corresponding to empty DirectShape.</param>
      /// <returns>ElementId of new DirectShape if successful, ElementId.InvalidElementId otherwise.</returns>
      public ElementId CreateEmptyContainer(IFCProduct ifcProduct)
      {
         // If HybridMap is null or empty, no other Elements were imported using the ATF Pipeline, so this doesn't need to be created.
         if ((ifcProduct == null) || ((HybridMap?.Count ?? 0) == 0))
         {
            return ElementId.InvalidElementId;
         }

         // If Container is already in HybridMAp, DirectShape has already been created.
         ElementId containerElementId;
         if (HybridMap.TryGetValue(ifcProduct.GlobalId, out containerElementId))
         {
            return containerElementId;
         }

         DirectShape emptyContainerDS = IFCElementUtil.CreateElement(IfcDocument, ifcProduct.CategoryId, ifcProduct.GlobalId, null, ifcProduct.Id, ifcProduct.EntityType);
         if (emptyContainerDS == null)
         {
            return ElementId.InvalidElementId;
         }

         HybridMap.Add(ifcProduct.GlobalId, emptyContainerDS.Id);
         
         return emptyContainerDS.Id;
      }

      /// <summary>
      /// This will create a container DirectShape to represent an IFCGroup, which normally has neither geometry
      /// nor a Revit Element associated with it.
      /// </summary>
      /// <param name="ifcGroup">Identifies the IFCGroup associated with the DirectShape.</param>
      /// <returns>ElementId of new DirectShape.</returns>
      /// <exception cref="InvalidOperationException">Occurs if underlying HybridImporter object is null.</exception>
      public ElementId CreateContainer(IFCGroup ifcGroup)
      {
         if (IfcDocument == null)
         {
            // Throws an exception if Document is null, but still require return statement for compiler.
            Importer.TheLog.LogError(-1, "No document for Hybrid IFC Import", true);
            return ElementId.InvalidElementId;
         }

         if (ifcGroup == null)
         {
            Importer.TheLog.LogError(ifcGroup.Id, "Cannot Create DirectShape for IFCGroup entity", false);
            return ElementId.InvalidElementId;
         }

         DirectShape directShape = DirectShape.CreateElement(IfcDocument, IFCElementUtil.GetDSValidCategoryId(IfcDocument, ifcGroup.CategoryId, ifcGroup.Id));
         if (directShape == null)
         {
            return ElementId.InvalidElementId;
         }
         ElementId directShapeId = directShape.Id;

         // Get IFC Guids of related objects.
         // If no geometry duplicated or filters don't allow any elements, then elements list will be empty,
         IList<ElementId> elements = new List<ElementId>();
         if (ifcGroup.ContainerDuplicatesGeometry())
         {
            foreach (IFCObjectDefinition objectDefinition in ifcGroup.RelatedObjects)
            {
               if (ifcGroup.ContainerFilteredEntity(objectDefinition))
               {
                  ElementId objDefId = ElementId.InvalidElementId;
                  if (HybridMap?.TryGetValue(objectDefinition.GlobalId, out objDefId) ?? false)
                  {
                     elements.Add(objDefId);
                  }
               }
            }
         }

         // Create geometry for new DirectShape.
         if (elements.Count > 0)
         {
            IList<GeometryObject> geometryObjects = DuplicateDirectShapeGeometry(elements);
            directShape.SetShape(geometryObjects);
         }

         return directShapeId;
      }

      /// <summary>
      /// Handles special cases for DirectShape "Creation".
      /// Current special cases:
      /// 1. Structural Column -- duplicates all DirectShapes and referenced DirectShapeTypes, using a new Category (Structural Column).
      /// 2. Creates a Container Element for sub-Elements.  This doesn't create a new Element.  It just populates a DirectShape Element with sub-Element Geometry.
      ///    empty
      /// </summary>
      /// <param name="objectDefinition">Entity that may exhibit special-case behavior.</param>
      /// <returns>ElementId of new DirectShape if new Element created, ElementId.InvalidElement otherwise.</returns>
      public ElementId CreateElementForSpecialCases(IFCObjectDefinition objectDefinition)
      {
         if (objectDefinition == null)
         {
            Importer.TheLog.LogNullError(objectDefinition.EntityType);
            return ElementId.InvalidElementId;
         }

         ElementId newElementId = ElementId.InvalidElementId;
         // Special Cases:
         // 1. Columns that should be Structure Columns.
         // 2. Containers that have sub-elements.
         if (IFCCategoryUtil.IsSpecialColumnCase(objectDefinition))
         {
            newElementId = (objectDefinition is IFCProduct architecturalColumn) ? CreateStructuralColumnDirectShape(architecturalColumn) : ElementId.InvalidElementId;
         }
         else if (objectDefinition.IsHybridImportContainer())
         {
            newElementId = (objectDefinition is IFCProduct container) ? CreateContainer(container) : ElementId.InvalidElementId;
         }

         // If special case processing resulted in an error, or did not change the ElementId, there is no need to replace
         // the old ElementId with a new one.
         if ((newElementId != ElementId.InvalidElementId) && (newElementId != objectDefinition.CreatedElementId))
         {
            ReplaceElementId(objectDefinition.GlobalId, objectDefinition.CreatedElementId, newElementId);
         }

         return newElementId;
      }

      /// <summary>
      /// Populate Container DirectShape Element with sub-Element Geometry.
      /// This will not actually create the DirectShape.  The DirectShape should already exist and be within the HybridMap.
      /// </summary>
      /// <param name="containerProduct">IFCProduct representing the Container.</param>
      /// <returns>ElementId of the DirectShape if successful, ElementId.InvalidElementId otherwise.</returns>
      protected ElementId CreateContainer(IFCProduct containerProduct)
      {
         // If nothing is in the HybridMap, just return.  Nothing needs to be done.
         if ((containerProduct == null) || ((HybridMap?.Count ?? 0) == 0))
         {
            Importer.TheLog.LogNullError(containerProduct.EntityType);
         }

         int stepId = containerProduct.Id;
         string containerGuid = containerProduct.GlobalId;
         ElementId containerCategoryId = containerProduct.CategoryId;
         if ((stepId < 1) || string.IsNullOrEmpty(containerGuid) || (containerCategoryId == ElementId.InvalidElementId))
         {
            Importer.TheLog.LogError(stepId, "Cannot create Container Geometry for IfcProduct -- invalid entity", false);
            return ElementId.InvalidElementId;
         }

         if (HybridMap == null)
         {
            Importer.TheLog.LogWarning(stepId, $"No Sub-elements imported for {containerProduct.GlobalId} via Hybrid.  Nothing to reference", false);
            return ElementId.InvalidElementId;
         }

         IList<GeometryObject> newGeometryObjects = new List<GeometryObject>();

         // If there is any Geometry already within the DirectShape (there may be an Axis Curve for an IfcWall, for instance,
         // add it to the Shape used at the end.  This will overwrite the current Geometry.
         ElementId containerDirectShapeElementId = ElementId.InvalidElementId;
         if (!HybridMap.TryGetValue(containerGuid, out containerDirectShapeElementId))
         {
            return ElementId.InvalidElementId;
         }

         DirectShape containerDirectShape = IfcDocument.GetElement(containerDirectShapeElementId) as DirectShape;
         if (containerDirectShape == null)
         {
            return ElementId.InvalidElementId;
         }

         Options options = new Options();
         GeometryElement oldContainerGeometryElement = containerDirectShape?.get_Geometry(options);
         if (oldContainerGeometryElement != null)
         {
            foreach (GeometryObject geomObj in oldContainerGeometryElement)
            {
               if (geomObj != null)
               {
                  newGeometryObjects.Add(geomObj);
               }
            }
         }

         // Iterate through all the sub-elements.
         HashSet<IFCObjectDefinition> relatedObjects = null;
         if (!(ContainerMap?.TryGetValue(stepId, out relatedObjects) ?? false))
         {
            Importer.TheLog.LogComment(stepId, $"No Related Objects for Container {containerProduct.GlobalId}.", false);
            return ElementId.InvalidElementId;
         }

         foreach (IFCObjectDefinition relatedObject in relatedObjects)
         {
            // Get the DirectShape for each sub-element.  It should be there.
            ElementId subElementId;
            if (!HybridMap.TryGetValue(relatedObject.GlobalId, out subElementId))
            {
               Importer.TheLog.LogWarning(stepId, $"{relatedObject.GlobalId} is not an Aggregate of {containerProduct.GlobalId}", false);
               continue;
            }

            DirectShape subElementDirectShape = IfcDocument.GetElement(subElementId) as DirectShape;
            if (subElementDirectShape == null)
            {
               continue;
            }

            // Duplicate sub-Element Geometry using the Category of the Container Geometry.
            IList<GeometryObject> newSubElementGeometries = DuplicateGeometryForDirectShape(subElementDirectShape, containerProduct);
            if ((newSubElementGeometries?.Count ?? 0) == 0)
            {
               Importer.TheLog.LogError(stepId, "Unable to duplicate Geometry for DirectShape recategorization.", false);
               return ElementId.InvalidElementId;
            }

            // Reference new sub-Element Geometry.
            GeometryInstance newSubElementGeometryInstance = newSubElementGeometries.First() as GeometryInstance; ;
            if (newSubElementGeometryInstance == null)
            {
               Importer.TheLog.LogWarning(stepId, "Duplicate Geometry is not a GeometryInstance.  Using old Geometry.", false);
               return ElementId.InvalidElementId;
            }

            newGeometryObjects.Add(newSubElementGeometryInstance);
         }

         // Set the Geometry of the containerDirectShape to newGeometryObjects.
         containerDirectShape.SetShape(newGeometryObjects);
         return containerDirectShape.Id;
      }

      /// <summary>
      /// Create a new DirectShape for the new Structural Column.
      /// </summary>
      /// <preconditions></preconditions>
      /// <param name="ifcColumn">Column that needs a Category change from OST_Column to OST_StructuralColumn.</param>
      /// <returns>ElementId of new Structural Column for successful creation, ElementId.InvalidElementId otherwise.</returns>
      protected ElementId CreateStructuralColumnDirectShape(IFCProduct ifcColumn)
      {
         if (ifcColumn == null)
         {
            Importer.TheLog.LogError(-1, "IfcColumn invalid during DirectShape recategorization.", false);
            return ElementId.InvalidElementId;
         }

         int stepId = ifcColumn.Id;
         ElementId ifcColumnCategory = ifcColumn.CategoryId;
         if (ifcColumnCategory != new ElementId(BuiltInCategory.OST_StructuralColumns))
         {
            Importer.TheLog.LogWarning(stepId, "IfcColumn is not a Structural Column", false);
            return ElementId.InvalidElementId;
         }

         ElementId oldDirectShapeElementId = ifcColumn.CreatedElementId;
         DirectShape oldDirectShape = IfcDocument.GetElement(oldDirectShapeElementId) as DirectShape;
         ElementId oldDirectShapeCategory = (oldDirectShape?.Category?.Id ?? ElementId.InvalidElementId);
         if (oldDirectShapeCategory == ElementId.InvalidElementId)
         {
            Importer.TheLog.LogWarning(stepId, "Unable to determine Category of DirectShape.", false);
            return ElementId.InvalidElementId;
         }

         if (oldDirectShapeCategory == ifcColumnCategory)
         {
            Importer.TheLog.LogComment(stepId, "Category of Column and DirectShape agree. No recategorization needed.", false);
            return ElementId.InvalidElementId;
         }

         // Perform Deep copy of Geometry and DirectShapeTypes.
         IList<GeometryObject> newGeomObjects = DuplicateGeometryForDirectShape(oldDirectShape, ifcColumn);
         if ((newGeomObjects?.Count ?? 0) == 0)
         {
            Importer.TheLog.LogError(stepId, "Unable to duplicate Geometry for DirectShape recategorization.", false);
            return ElementId.InvalidElementId;
         }

         GeometryInstance newDirectShapeGeometryInstance = newGeomObjects.First() as GeometryInstance; ;
         if (newDirectShapeGeometryInstance == null)
         {
            Importer.TheLog.LogWarning(stepId, "Duplicate Geometry is not a GeometryInstance.  Using old Geometry.", false);
            return ElementId.InvalidElementId;
         }

         ElementId directShapeTypeId = newDirectShapeGeometryInstance.GetSymbolGeometryId().SymbolId;
         if (directShapeTypeId == ElementId.InvalidElementId)
         {
            Importer.TheLog.LogWarning(stepId, "Even though new DirectShape Geometry created, unable to find DirectShapeType.", false);
            return ElementId.InvalidElementId;
         }

         IList<GeometryObject> structuralColumnGeometry = new List<GeometryObject>();
         structuralColumnGeometry.Add(newDirectShapeGeometryInstance);

         DirectShape newDirectShape = IFCElementUtil.CreateElement(IfcDocument, ifcColumnCategory, ifcColumn.GlobalId, structuralColumnGeometry, stepId, ifcColumn.EntityType);
         if (newDirectShape == null)
         {
            Importer.TheLog.LogError(stepId, "Unable to create new DirectShape Element for Structural Column.", false);
            return ElementId.InvalidElementId;
         }

         newDirectShape.SetTypeId(directShapeTypeId);
         return newDirectShape.Id;
      }

      /// <summary>
      /// Duplicates Geometry within DirectShape for a new DirectShape creation.
      /// This is to drive the process where the DirectShape -> GInstance -> DirectShapeType -> etc. will be preserved.
      /// </summary>
      /// <param name="oldDirectShapeElementId">ElementId for the exiting DirectShape.</param>
      /// <param name="ifcProduct">IfcProduct corresponding to the DirectShape.</param>
      /// <returns>List of Geometry Objects for the new DirectShape.</returns>
      protected IList<GeometryObject> DuplicateGeometryForDirectShape(DirectShape oldDirectShape, IFCProduct ifcProduct)
      {
         int stepId = ifcProduct?.Id ?? -1;
         if ((oldDirectShape == null) || (stepId <= 0))
         {
            return null;
         }

         // DirectShape should have one and only one GeometryInstance, and no other GeometryObjects.
         Options options = new Options();
         GeometryElement geometryElement = oldDirectShape?.get_Geometry(options);
         if ((geometryElement?.Count() ?? 0) == 0)
         {
            return null;
         }

         GeometryInstance oldDirectShapeGeometryInstance = geometryElement?.First() as GeometryInstance;
         GeometryElement oldDirectShapeTypeGeometryElement = oldDirectShapeGeometryInstance?.SymbolGeometry;
         if (oldDirectShapeTypeGeometryElement == null)
         {
            return null;
         }

         ElementId oldDirectShapeTypeElementId = oldDirectShapeGeometryInstance?.GetSymbolGeometryId().SymbolId ?? ElementId.InvalidElementId;
         if (oldDirectShapeTypeElementId == ElementId.InvalidElementId)
         {
            return null;
         }

         // Reminder:  the passed-in IfcProduct may be a Container for the DirectShape.  
         // The container should have an associated IfcTypeObject, but it's not required as per spec.
         IFCTypeObject ifcTypeObject = null;
         HashSet<IFCTypeObject> typeObjects = ifcProduct.TypeObjects;
         if ((typeObjects?.Count ?? 0) > 0)
         {
            ifcTypeObject = typeObjects.First();
         }

         // Most of the work happens here to Copy DirectShapeTypes.
         ElementId newDirectShapeTypeId = DeepCopyDirectShapeType(oldDirectShapeTypeElementId, oldDirectShapeTypeGeometryElement, ifcProduct.CategoryId, ifcTypeObject);

         string definitionId = GetDirectShapeTypeDefinitionId(newDirectShapeTypeId);
         if (string.IsNullOrEmpty(definitionId))
         {
            return null;
         }

         // Create new GeoemtryInstance to add new DirectShapeType using the same Trf.
         // String = ifcTypeObject.GlobalId + oldDirectShapeTypeElementId (as a string).
         IList<GeometryObject> newGeomObjects = DirectShape.CreateGeometryInstance(IfcDocument, definitionId, oldDirectShapeGeometryInstance.Transform);
         return newGeomObjects;
      }

      /// <summary>
      /// Retrieves definition ID to look up DirectShapeType for GeometryInsance creation.
      /// </summary>
      /// <param name="elementId">ElementId of DirectShapeType.</param>
      /// <returns>definitionId string, or string.Empty if unable to create definition ID.</returns>
      protected static string GetDirectShapeTypeDefinitionId(ElementId elementId)
      {
         if (elementId == ElementId.InvalidElementId)
         {
            return string.Empty;
         }

         return $"DeepCopyDirectShapeType.{elementId}";
      }

      /// <summary>
      /// This will perform a Deep Copy of a DirectShapeType.
      /// This will iterate through all GeometryObjects contained within the passed-in GeometryElement, which has the Geometry for
      /// oldDirectShapeTypeElementId.
      /// Each GeometryObject is either a GeometryInstance or a Solid.
      /// For a GeometryInstance, that will specify another DirectShapeType, so do a DeepCopyDirectShapeType() on that DirectShapeType 
      /// and create a new GeometryInstance pointing to the new DirectShapeType created during the copy.  Add the new GeometryInstance to the new Geometry List.
      /// For a Solid, just add the Solid to the new Geometry List.
      /// Once all GeometryObjects are collected, create a new DirectShapeType and store all GeometryObjects within the new DirectShapeType.
      /// Add a definition to the new type within the DirectShapeLibrary so callers can reference this new DirectShapeType when they need to.
      /// </summary>
      /// <param name="oldDirectShapeTypeElementId">ElementId for existing DirectShapeType.</param>
      /// <param name="oldDirectShapeTypeGeometryElement">GeometryElement for existing DirectShapeType.</param>
      /// <param name="newCategoryId">Category that DirectShapeTypes should be.</param>
      /// <param name="ifcTypeObject">IFCTypeObject step that drives this.  If null, values from old DirectShapeType will be used.</param>
      /// <returns>ElementId of new DirectShapeType, or ElementId.Invalid if unable to create new DirectShapeType at any step.</returns>
      protected ElementId DeepCopyDirectShapeType(ElementId oldDirectShapeTypeElementId, GeometryElement oldDirectShapeTypeGeometryElement, 
                                                  ElementId newCategoryId, IFCTypeObject ifcTypeObject = null)
      {
         if ((oldDirectShapeTypeElementId == ElementId.InvalidElementId) || (oldDirectShapeTypeGeometryElement == null))
         {
            return ElementId.InvalidElementId;
         }

         DirectShapeType oldDirectShapeType = IfcDocument.GetElement(oldDirectShapeTypeElementId) as DirectShapeType;

         IList<GeometryObject> newGeomObjs = new List<GeometryObject>();
         foreach (GeometryObject geomObj in oldDirectShapeTypeGeometryElement)
         {
            if (geomObj is GeometryInstance)
            {
               GeometryInstance geomInstance = geomObj as GeometryInstance;
               GeometryElement otherOldDirectShapeTypeGeometryElement = geomInstance?.SymbolGeometry;
               if (otherOldDirectShapeTypeGeometryElement == null)
               {
                  continue;
               }

               ElementId otherOldDirectShapeTypeElementId = geomInstance.GetSymbolGeometryId().SymbolId;
               if (otherOldDirectShapeTypeElementId == ElementId.InvalidElementId)
               {
                  continue;
               }

               ElementId otherNewDirectShapeTypeElementId = DeepCopyDirectShapeType(otherOldDirectShapeTypeElementId, otherOldDirectShapeTypeGeometryElement,
                                                                                    newCategoryId, ifcTypeObject);
               if (otherNewDirectShapeTypeElementId == ElementId.InvalidElementId)
               {
                  continue;
               }

               // otherDefinitionId is the index into the DirectShapeLibrary for the other DirectShapeType.
               string otherDefinitionId = GetDirectShapeTypeDefinitionId(otherNewDirectShapeTypeElementId);
               if (string.IsNullOrEmpty(otherDefinitionId))
               {
                  continue;
               }

               // Create a new GeometryInstance to the new DirectShapeType Element.
               IList<GeometryObject> newGeometryObjectsForGeometryInstance = DirectShape.CreateGeometryInstance(IfcDocument, otherDefinitionId, geomInstance.Transform);
               foreach (GeometryObject newGeometryObjectForGeometryInstance in newGeometryObjectsForGeometryInstance)
               {
                  GeometryInstance geometryInstance = newGeometryObjectForGeometryInstance as GeometryInstance;
                  if (geometryInstance == null)
                  {
                     continue;
                  }
                  newGeomObjs.Add(newGeometryObjectForGeometryInstance);
               }
            }
            else if (geomObj != null)
            {
               newGeomObjs.Add(geomObj);
            }
         }

         DirectShapeType newDirectShapeType = DuplicateDirectShapeType(oldDirectShapeType, newCategoryId, ifcTypeObject);
         if (newDirectShapeType == null)
         {
            return ElementId.InvalidElementId;
         }

         // In ATF Pipeline, oldDirectShapeType has had its DirectShapeType family changed.
         // If this is the case, do the same with the new DirectShapeType.
         if (newDirectShapeType.CanChangeFamilyName())
         {
            newDirectShapeType.SetFamilyName(oldDirectShapeType.FamilyName);
         }

         // Need to store this so parents can create a new GeometryInstance
         ElementId newDirectShapeTypeElementId = newDirectShapeType.Id;

         // definitionId is the index into the DirectShapeLibrary for this DirectShapeType.
         string definitionId = GetDirectShapeTypeDefinitionId(newDirectShapeTypeElementId);
         if (string.IsNullOrEmpty(definitionId))
         {
            return ElementId.InvalidElementId;
         }

         // Only set the shape if definition ID was valid.
         newDirectShapeType.SetShape(newGeomObjs);

         DirectShapeLibrary dsl = DirectShapeLibrary.GetDirectShapeLibrary(IfcDocument);
         if (dsl == null)
         {
            return ElementId.InvalidElementId;
         }

         dsl.AddDefinitionType(definitionId, newDirectShapeTypeElementId);
         return newDirectShapeTypeElementId;
      }

      /// <summary>
      /// Creates new DirectShapeType to hold duplicated GeometryObjects.
      /// </summary>
      /// <param name="oldDirectShapeType">DirectShapeType to be duplicated.</param>
      /// <param name="newCategoryId">Category of new DirectShapeTyhpe.</param>
      /// <param name="ifcTypeObject">IfcTypeObject to use for DirectShapeTyhpe. (may be null).</param>
      /// <returns>New DirectShapeTyhpe if successful, null otherwise.</returns>
      protected DirectShapeType DuplicateDirectShapeType(DirectShapeType oldDirectShapeType, ElementId newCategoryId, IFCTypeObject ifcTypeObject = null)
      {
         if ((oldDirectShapeType == null) || ((newCategoryId ?? ElementId.InvalidElementId) == ElementId.InvalidElementId))
         {
            return null;
         }

         ElementId categoryToUse = (DirectShape.IsValidCategoryId(newCategoryId, IfcDocument)) ? newCategoryId : oldDirectShapeType.Category.Id;

         DirectShapeTypeOptions options = new DirectShapeTypeOptions();
         options.AllowDuplicateNames = true;

         string nameToUse = ifcTypeObject?.GetVisibleName() ?? oldDirectShapeType.Name;
         return DirectShapeType.Create(IfcDocument, nameToUse, categoryToUse, options);
      }

      /// <summary>
      /// Duplicates the geometry contained within all the passed-in DirectShape Elements.
      /// </summary>
      /// <remarks>
      /// The returned List of GeometryObjects may not have a 1:1 relationship with the passed-in elements.
      /// This works because we are using DirectShapes.  Without that underlying assumption, Element.get_Geometry() might not work as expected.
      /// This might also require some re-work to avoid Element expansion.
      /// </remarks>
      /// <param name="otherElements">Elements that will have geometry duplicated.</param>
      /// <returns>List of duplicated GeometryObjects.</returns>
      public IList<GeometryObject> DuplicateDirectShapeGeometry(IList<ElementId> directShapeElementIds)
      {
         IList<GeometryObject> geometryObjects = new List<GeometryObject>();
         foreach (ElementId elementId in directShapeElementIds)
         {
            DirectShape otherDirectShape = (IfcDocument.GetElement(elementId)) as DirectShape;
            if (otherDirectShape == null)
            {
               continue;
            }

            Options options = new Options();
            GeometryElement geometryElement = otherDirectShape.get_Geometry(options);
            if (geometryElement == null)
            {
               continue;
            }
            
            foreach (GeometryObject geometryObject in geometryElement)
            {
               // For Hybrid IFC Import, it is sufficient to check for GeometryInstances only.
               // For other cases, Solids and Meshes should be considered.
               if (geometryObject is GeometryInstance)
               {
                  geometryObjects.Add(geometryObject);
               }
               else if (!Importer.TheOptions.IsHybridImport)
               {
                  if ((geometryObject is Mesh) || (geometryObject is Solid))
                  {
                     geometryObjects.Add(geometryObject);
                  }
               }
            }
         }
         return geometryObjects;
      }

      /// <summary>
      /// IFCProduct creation for Hybrid IFC Import takes on a different meaning than "creation" for Legacy IFC Import.
      /// Prior to creation, there should already be DirectShape/DirectShapeTypes created for the IFCProduct.  Geometry should already be created.
      /// But there are some data within Representation Items that need to persist to the new DirectShapes, and the only way to get that is to process the ProductRepresentation within the IFCProduct.
      /// So this populates that data.
      /// </summary>
      /// <param name="shapeEditScope">Some data is contained in the ShapeEditScope (but not actual geometry).</param>
      /// <param name="ifcProduct">IFCProduct to edit.</param>
      public ElementId HandleHybridProductCreation(IFCImportShapeEditScope shapeEditScope, IFCProduct ifcProduct)
      {
         if (!Importer.TheOptions.IsHybridImport || (shapeEditScope == null) || (ifcProduct == null) || (ifcProduct.CreatedElementId == ElementId.InvalidElementId))
         {
            return ElementId.InvalidElementId;
         }

         ElementId hybridElementId = ifcProduct.CreatedElementId;

         // Get DirectShape to "Create".  It's already created, so Revit is not really creating it here.
         DirectShape directShape = IfcDocument.GetElement(hybridElementId) as DirectShape;
         if (directShape == null)
         {
            return ElementId.InvalidElementId;
         }

         // Get solids for IFCProduct.  Only Points and Curves should be contained within "Solids".
         if (ifcProduct.Solids?.Count > 0)
         {
            WireframeBuilder wireframeBuilder = new WireframeBuilder();
            foreach (IFCSolidInfo solidInfo in ifcProduct.Solids)
            {
               GeometryObject currObject = solidInfo.GeometryObject;
               if (currObject is Point)
               {
                  wireframeBuilder.AddPoint(currObject as Point);
               }
               else if (currObject is Curve)
               {
                  wireframeBuilder.AddCurve(currObject as Curve);
               }
            }

            directShape.AppendShape(wireframeBuilder);
         }

         // Add Plan View Curves.
         if (shapeEditScope.AddPlanViewCurves(ifcProduct.FootprintCurves, ifcProduct.Id))
         {
            shapeEditScope.SetPlanViewRep(directShape);
         }

         // IFCProduct needs PresentationLayer parameter, which is contained in the RepresentationItem (or IFCHybridRepresentationItem).
         ifcProduct.PresentationLayerNames.UnionWith(shapeEditScope.PresentationLayerNames);

         // Handle Special Cases:
         // 1.  Possible Category Change for Structural Columns.  This requires a whole new DirectShape/DirectShapeType tree creation.
         // 2.  Containers for IfcRelAggregates (e.g., IfcWall & IfcBuildingElementParts).
         // In either of these, the specialCaseElementId may end up being the same as the original CreatedElementId.
         // If new ElementId is in
         // This means that there was no new Element created, so don't delete the ElementId in that case.
         ElementId newCreatedId = hybridElementId;
         ElementId specialCaseElementId = Importer.TheHybridInfo.CreateElementForSpecialCases(ifcProduct);
         if ((specialCaseElementId != ElementId.InvalidElementId) && (specialCaseElementId != hybridElementId))
         {
            // specialCaseElementId has replaced hybridElementId in the HybridMap.
            // The hybridElementId will be deleted later.
            Importer.TheHybridInfo.ElementsToDelete.Add(hybridElementId);
            newCreatedId = specialCaseElementId;
         }

         return newCreatedId;
      }

      /// <summary>
      /// Adds IFCTypeObject GUID and DirectShapeType ElementId to HybridMap.
      /// </summary>
      /// <remarks>
      /// Parameter 1:  corresponds to DirectShape/IfcProduct.
      /// Parameter 2:  corresponds to DirectShapeType/IfcTypeProduct.
      /// </remarks>
      /// <param name="ifcObjectGuid">Guid of IFCObject corresponding to DirectShape Element.</param>
      /// <param name="ifcTypeObject">Handle for IFCTypeObject.</param>
      public void AddTypeToHybridMap (string ifcObjectGuid, IFCAnyHandle ifcTypeObject)
      {
         if (string.IsNullOrEmpty(ifcObjectGuid))
            return;

         if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcTypeObject))
         {
            Importer.TheLog.LogNullError(IFCEntityType.IfcTypeObject);
            return;
         }

         if (HybridMap == null)
         {
            Importer.TheLog.LogError(ifcTypeObject.Id, "HybridMap is null while trying to process IFCTypeObject for IFCObject.  This shouldn't happen", true);
            return;
         }

         string ifcTypeObjectGuid = IFCImportHandleUtil.GetRequiredStringAttribute(ifcTypeObject, "GlobalId", false);
         if ((string.IsNullOrEmpty(ifcTypeObjectGuid)) || HybridMap.ContainsKey(ifcTypeObjectGuid))
         {
            Importer.TheLog.LogComment(ifcTypeObject.Id, $"Already added IFC GUID {ifcTypeObjectGuid} to HybridMap.  Not an error.", true);
            return;
         }

         ElementId directShapeElementId = ElementId.InvalidElementId;
         if (HybridMap.TryGetValue(ifcObjectGuid, out directShapeElementId))
         {
            DirectShape directShape = IfcDocument.GetElement(directShapeElementId) as DirectShape;
            if (directShape == null)
               return;

            ElementId directShapeTypeElementId = directShape.TypeId;
            if (directShapeTypeElementId != ElementId.InvalidElementId)
            {
               HybridMap.Add(ifcTypeObjectGuid, directShapeTypeElementId);
            }
         }
      }
   }
}
