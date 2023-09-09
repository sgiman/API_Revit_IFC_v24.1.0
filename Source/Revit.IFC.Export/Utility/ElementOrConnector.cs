/*******************************************************************************
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
using Autodesk.Revit.DB;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// A simple class to store eigther element or connector.
   /// </summary>
   public class ElementOrConnector
   {
      /// <summary>
      /// The element object
      /// </summary>
      public Element Element { get; set; } = null;

      /// <summary>
      /// The connector object
      /// </summary>
      public Connector Connector { get; set; } = null;

      /// <summary>
      /// Initialize the class with the element
      /// </summary>
      /// <param name="element">The element</param>
      public ElementOrConnector(Element element)
      {
         Element = element;
      }

      /// <summary>
      /// Initialize the class with the connector
      /// </summary>
      /// <param name="connector">The connector</param>
      public ElementOrConnector(Connector connector)
      {
         Connector = connector;
      }
   }
}
