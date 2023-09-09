/*******************************************************************************
////////////////////////////////////////////////////////////////////////////////
 * 
 * Exporter.cs
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
 * Modification by sgiman @ 2023
 * 
////////////////////////////////////////////////////////////////////////////////
*******************************************************************************/
//
// Библиотека BIM IFC: эта библиотека работает с Autodesk(R) Revit(R)
// для экспорта файлов IFC, содержащих геометрию модели.
// Авторские права (C) 2012 Autodesk, Inc.
//
// Эта библиотека является свободным программным обеспечением;
// вы можете распространять его и/или изменяем его в соответствии
// с условиями GNU Lesser General Public Лицензия,
// опубликованная Фондом свободного программного обеспечения;
// или версия 2.1 Лицензии или (по вашему выбору) любая более поздняя версия.
//
// Эта библиотека распространяется в надежде, что она будет полезна,
// но БЕЗ КАКИХ-ЛИБО ГАРАНТИЙ; даже без подразумеваемой гарантии
// ТОВАРНАЯ ПРИГОДНОСТЬ или ПРИГОДНОСТЬ ДЛЯ ОПРЕДЕЛЕННОЙ ЦЕЛИ.
// См. GNU Меньшая стандартная общественная лицензия для более подробной информации.
//
// Вы должны были получить копию GNU Lesser General Public.
// Лицензия вместе с этой библиотекой; если нет, напишите в Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Revit.IFC.Export.Exporter.PropertySet;
using Revit.IFC.Export.Utility;
using Revit.IFC.Common.Extensions;
using Revit.IFC.Export.Toolkit;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;
using Autodesk.Revit.DB.ExternalService;
using Revit.IFC.Export.Properties;
using System.Reflection;
using Autodesk.Revit.DB.Steel;


namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Этот класс реализует методы интерфейса IExternalDBApplication для регистрации клиента экспорта IFC в Autodesk Revit.
   /// </summary>
   class ExporterApplication : IExternalDBApplication
   {
      #region IExternalDBApplication Members

      /// <summary>
      /// Метод вызывается, когда Autodesk Revit выходит.
      /// </summary>
      /// <param name="application">Контроль приложения для завершением работы.</param>
      /// <returns>Возврат стутуса внешнего приложения.</returns>
      public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication application)
      {
         return ExternalDBApplicationResult.Succeeded;
      }

      /// <summary>
      /// Метод, вызываемый при запуске Autodesk Revit.
      /// </summary>
      /// <param name="application">Управляемое приложение, которое нужно загрузить в процесс Autodesk Revit.</param>
      /// <returns>Вернем статус внешнего приложения.</returns>
      public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication application)
      {
         // Как внешний сервер экспортёр не может быть зарегистрирован до полной инициализации приложения.
         // Для этого настройте обратный вызов события в подходящее время.
         application.ApplicationInitialized += OnApplicationInitialized;
         return ExternalDBApplicationResult.Succeeded;
      }

      #endregion

      /// <summary>
      /// Действие, выполняемое при инициализации приложения.
      /// </summary>
      /// <param name="sender">Отправитель.</param>
      /// <param name="eventArgs">Аргументы события.</param>
      private void OnApplicationInitialized(object sender, EventArgs eventArgs)
      {
         SingleServerService service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.IFCExporterService) as SingleServerService;
         if (service != null)
         {
            Exporter exporter = new Exporter();
            service.AddServer(exporter);
            service.SetActiveServer(exporter.GetServerId());
         }
         // TODO соответствующим образом зарегистрировать этот сбой
      }
   }

   /// <summary>
   /// Этот класс реализует метод интерфейса IExporterIFC для выполнения экспорта в IFC.
   /// </summary>
   public class Exporter : IExporterIFC
   {
      RevitStatusBar statusBar = null;

      // Используется для инструмента отладки "WriteIFCExportedElements".
      private StreamWriter m_Writer;

      private IFCFile m_IfcFile;

      // Разрешить производному классу добавлять процедуры экспорта элементов.
      public delegate void ElementExporter(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document);

      protected ElementExporter m_ElementExporter = null;

      // Разрешить производному классу добавлять наборы свойств.
      public delegate void PropertySetsToExport(IList<IList<PropertySetDescription>> propertySets);

      // Разрешить производному классу добавлять предопределенные наборы свойств.
      public delegate void PreDefinedPropertySetsToExport(IList<IList<PreDefinedPropertySetDescription>> propertySets);

      // Разрешить производному классу добавлять количества.
      public delegate void QuantitiesToExport(IList<IList<QuantityDescription>> propertySets);

      protected QuantitiesToExport m_QuantitiesToExport = null;
      

      #region IExporterIFC Members

      /// <summary>     
      /// Создаем список процедур экспорта элементов. 
      /// Каждая процедура экспортирует подмножество элементов Revit.
      /// позволяющий выбирать, какие элементы экспортироваться и в каком порядке.
      /// Эта процедура защищена, поэтому ее может переопределить класс Exporter, наследуемый от этого базового класса.
      /// </summary>
      protected virtual void InitializeElementExporters()
      {
         // Разрешить другой функции потенциально добавлять экспортеры перед ExportSpatialElements.
         if (m_ElementExporter == null)
            m_ElementExporter = ExportSpatialElements;
         else
            m_ElementExporter += ExportSpatialElements;

         m_ElementExporter += ExportNonSpatialElements;
         m_ElementExporter += ExportContainers;
         m_ElementExporter += ExportGrids;
         m_ElementExporter += ExportConnectors;
         
         // export AdvanceSteel elements
         m_ElementExporter += ExportAdvanceSteelElements;
      }

      private void ExportHostDocument(ExporterIFC exporterIFC, Document document, View filterView)
      {
         BeginExport(exporterIFC, filterView);
         BeginHostDocumentExport(exporterIFC, document);

         m_ElementExporter?.Invoke(exporterIFC, document);

         EndHostDocumentExport(exporterIFC, document);
      }

      private void ExportLinkedDocument(ExporterIFC exporterIFC, ElementId linkId, Document document,
         string guid, Transform linkTrf)
      {
         using (IFCLinkDocumentExportScope linkScope = new IFCLinkDocumentExportScope(document))
         {
            ExporterStateManager.CurrentLinkId = linkId;
            exporterIFC.SetCurrentExportedDocument(document);

            BeginLinkedDocumentExport(exporterIFC, document, guid);
            m_ElementExporter?.Invoke(exporterIFC, document);
            EndLinkedDocumentExport(exporterIFC, document, linkTrf);
         }
      }

      //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      /// <summary>
      /// Реализует метод, который Autodesk Revit будет вызывать для выполнения экспорта в IFC.
      /// </summary>    
      /// <param name="document">Документ для экспорта.</param>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="filterView">Представление, настройки видимости фильтра которого управляют экспортом.</param>
      /// <remarks>Обратите внимание, что filterView не управляет экспортированной геометрией; он только контролирует, какие элементы
      /// видны или нет. Это позволяет нам, например, выбрать вид сверху, но получить трехмерную геометрию.</remarks>
      //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
      public void ExportIFC(Document document, ExporterIFC exporterIFC, View filterView)
      {
         // Убедитесь, что наши статические кэши очищены в начале и в конце экспорта.
         ExporterCacheManager.Clear(true);
         ExporterStateManager.Clear();

         try
         {
            IFCAnyHandleUtil.IFCStringTooLongWarn += (_1) => { document.Application.WriteJournalComment(_1, true); };
            IFCDataUtil.IFCStringTooLongWarn += (_1) => { document.Application.WriteJournalComment(_1, true); };

            ParamExprListener.ResetParamExprInternalDicts();
            InitializeElementExporters();

            ExportHostDocument(exporterIFC, document, filterView);

            IDictionary<long, string> linkInfos =
               ExporterCacheManager.ExportOptionsCache.FederatedLinkInfo;
            if (linkInfos != null)
            {
               foreach (KeyValuePair<long, string> linkInfo in linkInfos)
               {
                  // ЗАДАЧА: отфильтровать типы ссылок, которые мы не поддерживаем,
                  // и предупредить, как мы делаем при экспорте отдельных ссылок.
                  ElementId linkId = new ElementId(linkInfo.Key);
                  RevitLinkInstance rvtLinkInstance = document.GetElement(linkId) as RevitLinkInstance;
                  if (rvtLinkInstance == null)
                     continue;

                  // Не экспортируйте ссылку, если она скрыта в представлении.
                  // Стандарт фильтр, чтобы определить, можем ли мы экспортировать элемент,
                  // вернет false для ссылки.
                  GeometryElement geomElem = null;
                  if (filterView != null)
                  {
                     Options options = new Options() { View = filterView };
                     geomElem = rvtLinkInstance.get_Geometry(options);
                     if (geomElem == null)
                        continue;
                  }

                  Document linkedDocument = rvtLinkInstance.GetLinkDocument();
                  if (linkedDocument != null)
                  {
                     Transform linkTrf = rvtLinkInstance.GetTransform();
                     ExporterCacheManager.Clear(false);
                     ExportLinkedDocument(exporterIFC, rvtLinkInstance.Id, linkedDocument, 
                        linkInfo.Value, linkTrf);
                  }
               }
            }

            IFCFileDocumentInfo ifcFileDocumentInfo = new IFCFileDocumentInfo(document);

            WriteIFCFile(m_IfcFile, ifcFileDocumentInfo);
         
            }
         catch
         {
            // Начиная с Revit 2022.1, ошибки EDM, о которых нужно сообщать, нет, поскольку мы используем ODA.
            FailureMessage fm = new FailureMessage(BuiltInFailures.ExportFailures.IFCFatalExportError);
            document.PostFailure(fm);
         }
         finally
         {
            ExporterCacheManager.Clear(true);
            ExporterStateManager.Clear();

            DelegateClear();
            IFCAnyHandleUtil.EventClear();
            IFCDataUtil.EventClear();

            m_Writer?.Close();

            m_IfcFile?.Close();
            m_IfcFile = null; 
         }
      }

      public virtual string GetDescription()
      {
         return "IFC open source exporter";
      }

      public virtual string GetName()
      {
         return "IFC exporter";
      }

      public virtual Guid GetServerId()
      {
         return new Guid("BBE27F6B-E887-4F68-9152-1E664DAD29C3");
      }

      public virtual string GetVendorId()
      {
         return "IFCX";
      }

      // Это не виртуальный параметр, и его не следует переопределять.
      public Autodesk.Revit.DB.ExternalService.ExternalServiceId GetServiceId()
      {
         return Autodesk.Revit.DB.ExternalService.ExternalServices.BuiltInExternalServices.IFCExporterService;
      }

      #endregion

      /// <summary>
      /// Экспортирует специальные элементы AdvanceSteel.
      /// </summary>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      /// <param name="document">The Revit document.</param>
      protected void ExportAdvanceSteelElements(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         // проверяем, нужно ли экспортировать стальные элементы
         if (ExporterCacheManager.ExportOptionsCache.IncludeSteelElements)
         {
            try
            {
               // Сначала ищем сборку SteelConnections в папке надстроек.
               string dllPath = Assembly.GetExecutingAssembly().Location;
               Assembly assembly;
               if (File.Exists(Path.GetDirectoryName(dllPath) + @"\Autodesk.SteelConnections.ASIFC.dll"))
                  assembly = Assembly.LoadFrom(Path.GetDirectoryName(dllPath) + @"\Autodesk.SteelConnections.ASIFC.dll");
               else
                  assembly = Assembly.LoadFrom(AppDomain.CurrentDomain.BaseDirectory + @"\Addins\SteelConnections\Autodesk.SteelConnections.ASIFC.dll");

               if (assembly != null)
               {
                  Type type = assembly.GetType("Autodesk.SteelConnections.ASIFC.ASExporter");
                  if (type != null)
                  {
                     MethodInfo method = type.GetMethod("ExportASElements");
                     if (method != null)
                        method.Invoke(null, new object[] { exporterIFC, document });
                  }
               }
            }
            catch
            { }
         }
      }

      /// <summary>
      /// Создание основного сборщика элементов экспорта, используемый для фильтрации элементов.
      /// </summary>
      /// <param name="document">Документ.</param>
      /// <param name="useFilterViewIfExists">Если false, не использовать представление фильтра
      /// даже если он существует.</param>
      /// <returns>Коллектор FilteredElementCollector.</returns>
      /// <remarks>useFilterViewIfExists предназначено для случаев, когда значение false
      /// где мы хотим потенциально экспортировать некоторые невидимые элементы, такие как
      /// как комнаты в 3D-видах.</remarks>

      private FilteredElementCollector GetExportElementCollector(
         Document document, bool useFilterViewIfExists)
      {
         ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;
         ICollection<ElementId> idsToExport = exportOptionsCache.ElementsForExport;
         if (idsToExport.Count > 0)
         {
            return new FilteredElementCollector(document, idsToExport);
         }

         View filterView = useFilterViewIfExists ?
            exportOptionsCache.FilterViewForExport : null;

         if (filterView == null)
         {
            return new FilteredElementCollector(document);
         }

         if (ExporterStateManager.CurrentLinkId != ElementId.InvalidElementId)
         {
            return new FilteredElementCollector(filterView.Document, filterView.Id,
               ExporterStateManager.CurrentLinkId);
         }

         return new FilteredElementCollector(filterView.Document, filterView.Id);
      }

      /// <summary>
      /// Проверяет, содержится ли пространственный элемент внутри рамки сечения, если рамка существует.
      /// </summary>      
      /// <param name="sectionBox">Окно раздела.</param>
      /// <param name="element">Элемент.</param>
      /// <returns>False, если есть поле раздела и можно определить, что элемент не находится внутри него.</returns>
      private bool SpatialElementInSectionBox(BoundingBoxXYZ sectionBox, Element element)
      {
         if (sectionBox == null)
            return true;

         BoundingBoxXYZ elementBBox = element.get_BoundingBox(null);
         if (elementBBox == null)
         {
            // Области не имеют геометрии ограничивающего прямоугольника. Для этого попробуйте их точку местоположения.
            LocationPoint locationPoint = element.Location as LocationPoint;
            if (locationPoint == null)
               return false;

            elementBBox = new BoundingBoxXYZ();
            elementBBox.set_Bounds(0, locationPoint.Point);
            elementBBox.set_Bounds(1, locationPoint.Point);
         }

         return GeometryUtil.BoundingBoxesOverlap(elementBBox, sectionBox);
      }

      protected void ExportSpatialElements(ExporterIFC exporterIFC, Document document)
      {
         // Сначала создайте IfcSite, используя первую видимую поверхность TopographySurface,
         // если таковая имеется, если нет, создайте поверхность по умолчанию.
         // Сначала необходимо создать площадку и здание, чтобы обеспечить работу переопределения условий сдерживания.
         FilteredElementCollector topoElementCollector = GetExportElementCollector(document, true);
         List<Type> topoSurfaceType = new List<Type>() { typeof(TopographySurface) };
         ElementMulticlassFilter multiclassFilter = new ElementMulticlassFilter(topoSurfaceType);
         topoElementCollector.WherePasses(multiclassFilter);
         ICollection<ElementId> filteredTopoElements = topoElementCollector.ToElementIds();
         if (filteredTopoElements != null && filteredTopoElements.Count > 0)
         {
            foreach (ElementId topoElemId in filteredTopoElements)
            {
               // Обратите внимание, что экспортер TopographySurface в ExportElementImpl ничего не делает,
               // если элемент здесь уже обработан.
               Element topoElem = document.GetElement(topoElemId);
               if (ExportElement(exporterIFC, topoElem))
                  break; // Обрабатывайте только первый экспортируемый файл для создания IfcSite.
            }
         }

         if (ExporterCacheManager.SiteHandle == null || IFCAnyHandleUtil.IsNullOrHasNoValue(ExporterCacheManager.SiteHandle))
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               SiteExporter.ExportDefaultSite(exporterIFC, document, productWrapper);
               ExporterUtil.ExportRelatedProperties(exporterIFC, document.ProjectInformation, productWrapper);
            }
         }

         // Сначала создайте IfcBuilding здесь
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(ExporterCacheManager.BuildingHandle) && IFCAnyHandleUtil.IsNullOrHasNoValue(ExporterCacheManager.SiteHandle))
         {
            IFCAnyHandle buildingPlacement = CreateBuildingPlacement(exporterIFC.GetFile());
            IFCAnyHandle buildingHnd = CreateBuildingFromProjectInfo(exporterIFC, document, buildingPlacement);
            ExporterCacheManager.BuildingHandle = buildingHnd;
         }

         ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;
         View filterView = exportOptionsCache.FilterViewForExport;

         bool exportIfBoundingBoxIsWithinViewExtent = (exportOptionsCache.ExportRoomsInView && filterView is View3D);

         // Мы не хотим использовать представление фильтра для экспорта пробелов,
         // если ExportOptionsCache.ExportRoomsInView верно, и у нас есть 3D-вид.
         bool useFilterViewInCollector = !exportIfBoundingBoxIsWithinViewExtent;

         ISet<ElementId> exportedSpaces = null;
         if (exportOptionsCache.SpaceBoundaryLevel == 2)
            exportedSpaces = SpatialElementExporter.ExportSpatialElement2ndLevel(this, exporterIFC, document);

         // Экспортируем все пространственные элементы для границ комнаты без границ
         // или границ комнаты 1-го уровня; для 2-го уровня экспортируйте пространства, которые невозможно экспортировать выше.
         // Обратите внимание, что FilteredElementCollector используется только один раз, поэтому нам нужно создать здесь новый.
         FilteredElementCollector spatialElementCollector = GetExportElementCollector(document, useFilterViewInCollector);
         SpatialElementExporter.InitializeSpatialElementGeometryCalculator(document);
         ElementFilter spatialElementFilter = ElementFilteringUtil.GetSpatialElementFilter(document, exporterIFC);
         spatialElementCollector.WherePasses(spatialElementFilter);

         // Если вид 3D и поле сечения активно, то устанавливаем поле сечения
         BoundingBoxXYZ sectionBox = null;
         if (exportIfBoundingBoxIsWithinViewExtent)
         {
            View3D currentView = filterView as View3D;
            sectionBox = currentView != null && currentView.IsSectionBoxActive ? currentView.GetSectionBox() : null;
         }

         int numOfSpatialElements = spatialElementCollector.Count<Element>();
         int spatialElementCount = 1;

         foreach (Element element in spatialElementCollector)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingSpatialElements, spatialElementCount, numOfSpatialElements, element.Id));
            spatialElementCount++;

            if ((element == null) || (exportedSpaces != null && exportedSpaces.Contains(element.Id)))
               continue;
            if (ElementFilteringUtil.IsRoomInInvalidPhase(element))
               continue;
            // Если ограничивающая рамка элемента не пересекает рамку сечения, игнорируем ее.
            // Если поле раздела не активно, мы экспортируем элемент.
            if (!SpatialElementInSectionBox(sectionBox, element))
               continue;
            ExportElement(exporterIFC, element);
         }

         SpatialElementExporter.DestroySpatialElementGeometryCalculator();
      }

      protected void ExportNonSpatialElements(ExporterIFC exporterIFC, Document document)
      {
         FilteredElementCollector otherElementCollector = GetExportElementCollector(document, true);

         ElementFilter nonSpatialElementFilter = ElementFilteringUtil.GetNonSpatialElementFilter(document, exporterIFC);
         otherElementCollector.WherePasses(nonSpatialElementFilter);

         int numOfOtherElement = otherElementCollector.Count();
         IList<Element> otherElementCollListCopy = new List<Element>(otherElementCollector);
         int otherElementCollectorCount = 1;
         foreach (Element element in otherElementCollListCopy)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingNonSpatialElements, otherElementCollectorCount, numOfOtherElement, element.Id));
            otherElementCollectorCount++;
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Экспортируйте различные контейнеры, которые зависят от экспорта отдельных элементов
      /// </summary>
      /// <param name="document">Документ Revit.</param>
      /// <param name="exporterIFC">Класс экспортераIFC.</param>
      protected void ExportContainers(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         using (ExporterStateManager.ForceElementExport forceElementExport = new ExporterStateManager.ForceElementExport())
         {
            ExportCachedRailings(exporterIFC, document);
            ExportCachedFabricAreas(exporterIFC, document);
            ExportTrusses(exporterIFC, document);
            ExportBeamSystems(exporterIFC, document);
            ExportAreaSchemes(exporterIFC, document);
            ExportZones(exporterIFC, document);
         }
      }

      /// <summary>
      /// Экспортировать перила, кэшированные при экспорте пространственного элемента.
      /// Перила экспортируются последними, поскольку их содержание неизвестно до тех пор, пока не будут экспортированы все лестницы.
      /// Это очень простая сортировка, и дальнейшие проблемы сдерживания могут потребовать более надежного решения в будущем
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportCachedRailings(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> railingCollection = ExporterCacheManager.RailingCache;
         int railingIndex = 1;
         int railingCollectionCount = railingCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.RailingCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingRailings, railingIndex, railingCollectionCount, elementId));
            railingIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Экспортировать области FabricArea, кэшированные во время экспорта непространственных элементов.
      /// Мы экспортируем все, что на самом деле имеет дескрипторы FabricArea, как IfcGroup.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportCachedFabricAreas(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         IDictionary<ElementId, HashSet<IFCAnyHandle>> fabricAreaCollection = ExporterCacheManager.FabricAreaHandleCache;
         int fabricAreaIndex = 1;
         int fabricAreaCollectionCount = fabricAreaCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.FabricAreaHandleCache.Keys)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingFabricAreas, fabricAreaIndex, fabricAreaCollectionCount, elementId));
            fabricAreaIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Экспортные фермы. Они могут находиться в сборках, 
      /// поэтому делайте это перед экспортом сборки, но после экспорта балок и элементов.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportTrusses(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> trussCollection = ExporterCacheManager.TrussCache;
         int trussIndex = 1;
         int trussCollectionCount = trussCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.TrussCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingTrusses, trussIndex, trussCollectionCount, elementId));
            trussIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Экспортировать BeamSystems. Они могут находиться в сборках, 
      /// поэтому делайте это перед экспортом сборки, но после экспорта балок.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportBeamSystems(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> beamSystemCollection = ExporterCacheManager.BeamSystemCache;
         int beamSystemIndex = 1;
         int beamSystemCollectionCount = beamSystemCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.BeamSystemCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingBeamSystems, beamSystemIndex, beamSystemCollectionCount, elementId));
            beamSystemIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Экспортные зоны.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportZones(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         HashSet<ElementId> zoneCollection = ExporterCacheManager.ZoneCache;
         int zoneIndex = 1;
         int zoneCollectionCount = zoneCollection.Count;
         foreach (ElementId elementId in ExporterCacheManager.ZoneCache)
         {
            statusBar.Set(string.Format(Resources.IFCProcessingExportZones, zoneIndex, zoneCollectionCount, elementId));
            zoneIndex++;
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      /// <summary>
      /// Схемы экспортных площадей.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The exporterIFC class.</param>
      protected void ExportAreaSchemes(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         foreach (ElementId elementId in ExporterCacheManager.AreaSchemeCache.Keys)
         {
            Element element = document.GetElement(elementId);
            ExportElement(exporterIFC, element);
         }
      }

      protected void ExportGrids(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         // Экспортируйте сетки
         GridExporter.Export(exporterIFC, document);
      }

      protected void ExportConnectors(ExporterIFC exporterIFC, Autodesk.Revit.DB.Document document)
      {
         ConnectorExporter.Export(exporterIFC);
      }

      /// <summary>
      /// Определяет, соответствует ли выбранный элемент дополнительным критериям экспорта.
      /// </summary>
      /// <param name="exporterIFC">Класс экспортера.</param>
      /// <param name="element">Текущий элемент для экспорта.</param>
      /// <returns>True, если элемент нужно экспортировать.</returns>
      protected virtual bool CanExportElement(ExporterIFC exporterIFC, Autodesk.Revit.DB.Element element)
      {
         // Пропустите экспорт элементов AdvanceSteel, они будут экспортированы с помощью ExportASElements.
         if (ExporterCacheManager.ExportOptionsCache.IncludeSteelElements)
         {
            // Если Autodesk.Revit.DB.Steel отсутствует, продолжите экспорт.
            try
            {
               SteelElementProperties cell = SteelElementProperties.GetSteelElementProperties(element);
               if (cell != null)
               {
                  bool hasGraphics = false;
                  PropertyInfo graphicsCell = cell.GetType().GetProperty("HasGraphics", BindingFlags.Instance | BindingFlags.NonPublic);
                  if (graphicsCell != null) // Бетонные элементы с ячейкой, для которой HasGraphics имеет значение true, должны обрабатываться средством экспорта Revit.
                     hasGraphics = (bool)graphicsCell.GetValue(cell, null);

                  if (hasGraphics)
                     return false;
               }
            }
            catch
            { }
         }

         return ElementFilteringUtil.CanExportElement(exporterIFC, element, false);
      }

      /// <summary>
      /// Performs the export of elements, including spatial and non-spatial elements.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to export.</param>
      /// <returns>False if the element can't be exported at all, true otherwise.</returns>
      /// <remarks>A true return value doesn't mean something was exported, but that the
      /// routine did a quick reject on the element, or an exception occurred.</remarks>
      public virtual bool ExportElement(ExporterIFC exporterIFC, Element element)
      {
         if (!CanExportElement(exporterIFC, element))
         {
            if (element is RevitLinkInstance && ExporterUtil.ExportingHostModel())
            {
               bool bStoreIFCGUID = ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID;
               ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID = true;
               GUIDUtil.CreateGUID(element);
               ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID = bStoreIFCGUID;
            }
            return false;
         }

         //WriteIFCExportedElements
         if (m_Writer != null)
         {
            string categoryName = CategoryUtil.GetCategoryName(element);
            m_Writer.WriteLine(string.Format("{0},{1},{2}", element.Id, string.IsNullOrEmpty(categoryName) ? "null" : categoryName, element.GetType().Name));
         }

         try
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               if (element.AssemblyInstanceId != null && element.AssemblyInstanceId != ElementId.InvalidElementId)
               {
                  Element assemblyElem = element.Document.GetElement(element.AssemblyInstanceId);
                  ExportElementImpl(exporterIFC, assemblyElem, productWrapper);
               }
               ExportElementImpl(exporterIFC, element, productWrapper);
               ExporterUtil.ExportRelatedProperties(exporterIFC, element, productWrapper);
            }

            // We are going to clear the parameter cache for the element (not the type) after the export.
            // We do not expect to need the parameters for this element again, so we can free up the space.
            if (!(element is ElementType) && !ExporterStateManager.ShouldPreserveElementParameterCache(element))
               ParameterUtil.RemoveElementFromCache(element);
         }
         catch (System.Exception ex)
         {
            HandleUnexpectedException(ex, element);
            return false;
         }

         return true;
      }

      /// <summary>
      /// Handles the unexpected Exception.
      /// </summary>
      /// <param name="ex">The unexpected exception.</param>
      /// <param name="element ">The element got the exception.</param>
      internal void HandleUnexpectedException(Exception exception, Element element)
      {
         Document document = element.Document;
         string errMsg = string.Format("IFC error: Exporting element \"{0}\",{1} - {2}", element.Name, element.Id, exception.ToString());
         element.Document.Application.WriteJournalComment(errMsg, true);

         FailureMessage fm = new FailureMessage(BuiltInFailures.ExportFailures.IFCGenericExportWarning);
         fm.SetFailingElement(element.Id);
         document.PostFailure(fm);
      }

      /// <summary>
      /// Checks if the element is MEP type.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to check.</param>
      /// <returns>True for MEP type of elements.</returns>
      private bool IsMEPType(Element element, IFCExportInfoPair exportType)
      {
         return (ElementFilteringUtil.IsMEPType(exportType) || ElementFilteringUtil.ProxyForMEPType(element, exportType));
      }

      /// <summary>
      /// Checks if exporting an element as building elment proxy.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True for exporting as proxy element.</returns>
      private bool ExportAsProxy(Element element, IFCExportInfoPair exportType)
      {
         // FaceWall should be exported as IfcWall.
         return ((element is FaceWall) || (element is ModelText) || (exportType.ExportInstance == IFCEntityType.IfcBuildingElementProxy) || (exportType.ExportType == IFCEntityType.IfcBuildingElementProxyType));
      }

      /// <summary>
      /// Checks if exporting an element of Stairs category.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if element is of category OST_Stairs.</returns>
      private bool IsStairs(Element element)
      {
         return (CategoryUtil.GetSafeCategoryId(element) == new ElementId(BuiltInCategory.OST_Stairs));
      }

      /// <summary>
      /// Checks if the element is one of the types that contain structural rebar.
      /// </summary>
      /// <param name="element"></param>
      /// <returns></returns>
      private bool IsRebarType(Element element)
      {
         return (element is AreaReinforcement || element is PathReinforcement || element is Rebar || element is RebarContainer);
      }

      /// <summary>
      /// Implements the export of element.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="element">The element to export.</param>
      /// <param name="productWrapper">The ProductWrapper object.</param>
      public virtual void ExportElementImpl(ExporterIFC exporterIFC, Element element, ProductWrapper productWrapper)
      {
         View ownerView = ExporterCacheManager.ExportOptionsCache.UseActiveViewGeometry ?
            ExporterCacheManager.ExportOptionsCache.ActiveView :
            element.Document.GetElement(element.OwnerViewId) as View;

         Options options = (ownerView == null) ?
            GeometryUtil.GetIFCExportGeometryOptions() :
            new Options() { View = ownerView };
         
         GeometryElement geomElem = element.get_Geometry(options);

         // Default: we don't preserve the element parameter cache after export.
         bool shouldPreserveParameterCache = false;

         try
         {
            exporterIFC.PushExportState(element, geomElem);

            Autodesk.Revit.DB.Document doc = element.Document;
            using (SubTransaction st = new SubTransaction(doc))
            {
               st.Start();

               // A long list of supported elements.  Please keep in alphabetical order by the first item in the list..
               if (element is AreaScheme)
               {
                  AreaSchemeExporter.ExportAreaScheme(exporterIFC, element as AreaScheme, productWrapper);
               }
               else if (element is AssemblyInstance)
               {
                  AssemblyInstance assemblyInstance = element as AssemblyInstance;
                  AssemblyInstanceExporter.ExportAssemblyInstanceElement(exporterIFC, assemblyInstance, productWrapper);
               }
               else if (element is BeamSystem)
               {
                  if (ExporterCacheManager.BeamSystemCache.Contains(element.Id))
                     AssemblyInstanceExporter.ExportBeamSystem(exporterIFC, element as BeamSystem, productWrapper);
                  else
                  {
                     ExporterCacheManager.BeamSystemCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (element is Ceiling)
               {
                  Ceiling ceiling = element as Ceiling;
                  CeilingExporter.ExportCeilingElement(exporterIFC, ceiling, ref geomElem, productWrapper);
               }
               else if (element is CeilingAndFloor || element is Floor)
               {
                  // This covers both Floors and Building Pads.
                  CeilingAndFloor hostObject = element as CeilingAndFloor;
                  FloorExporter.ExportCeilingAndFloorElement(exporterIFC, hostObject, ref geomElem, productWrapper);
               }
               else if (element is WallFoundation)
               {
                  WallFoundation footing = element as WallFoundation;
                  FootingExporter.ExportFootingElement(exporterIFC, footing, geomElem, productWrapper);
               }
               else if (element is CurveElement)
               {
                  CurveElement curveElem = element as CurveElement;
                  CurveElementExporter.ExportCurveElement(exporterIFC, curveElem, geomElem, productWrapper);
               }
               else if (element is CurtainSystem)
               {
                  CurtainSystem curtainSystem = element as CurtainSystem;
                  CurtainSystemExporter.ExportCurtainSystem(exporterIFC, curtainSystem, productWrapper);
               }
               else if (CurtainSystemExporter.IsLegacyCurtainElement(element))
               {
                  CurtainSystemExporter.ExportLegacyCurtainElement(exporterIFC, element, productWrapper);
               }
               else if (element is DuctInsulation)
               {
                  DuctInsulation ductInsulation = element as DuctInsulation;
                  DuctInsulationExporter.ExportDuctInsulation(exporterIFC, ductInsulation, geomElem, productWrapper);
               }
               else if (element is DuctLining)
               {
                  DuctLining ductLining = element as DuctLining;
                  DuctLiningExporter.ExportDuctLining(exporterIFC, ductLining, geomElem, productWrapper);
               }
               else if (element is ElectricalSystem)
               {
                  ExporterCacheManager.SystemsCache.AddElectricalSystem(element.Id);
               }
               else if (element is FabricArea)
               {
                  // We are exporting the fabric area as a group only.
                  FabricSheetExporter.ExportFabricArea(exporterIFC, element, productWrapper);
               }
               else if (element is FabricSheet)
               {
                  FabricSheet fabricSheet = element as FabricSheet;
                  FabricSheetExporter.ExportFabricSheet(exporterIFC, fabricSheet, geomElem, productWrapper);
               }
               else if (element is FaceWall)
               {
                  WallExporter.ExportWall(exporterIFC, null, element, null, ref geomElem, productWrapper);
               }
               else if (element is FamilyInstance)
               {
                  FamilyInstance familyInstanceElem = element as FamilyInstance;
                  FamilyInstanceExporter.ExportFamilyInstanceElement(exporterIFC, familyInstanceElem, ref geomElem, productWrapper);
               }
               else if (element is FilledRegion)
               {
                  FilledRegion filledRegion = element as FilledRegion;
                  FilledRegionExporter.Export(exporterIFC, filledRegion, geomElem, productWrapper);
               }
               else if (element is Grid)
               {
                  ExporterCacheManager.GridCache.Add(element);
               }
               else if (element is Group)
               {
                  Group group = element as Group;
                  GroupExporter.ExportGroupElement(exporterIFC, group, productWrapper);
               }
               else if (element is HostedSweep)
               {
                  HostedSweep hostedSweep = element as HostedSweep;
                  HostedSweepExporter.Export(exporterIFC, hostedSweep, geomElem, productWrapper);
               }
               else if (element is Part)
               {
                  Part part = element as Part;
                  if (ExporterCacheManager.ExportOptionsCache.ExportPartsAsBuildingElements)
                     PartExporter.ExportPartAsBuildingElement(exporterIFC, part, geomElem, productWrapper);
                  else
                     PartExporter.ExportStandalonePart(exporterIFC, part, geomElem, productWrapper);
               }
               else if (element is PipeInsulation)
               {
                  PipeInsulation pipeInsulation = element as PipeInsulation;
                  PipeInsulationExporter.ExportPipeInsulation(exporterIFC, pipeInsulation, geomElem, productWrapper);
               }
               else if (element is Railing)
               {
                  if (ExporterCacheManager.RailingCache.Contains(element.Id))
                     RailingExporter.ExportRailingElement(exporterIFC, element as Railing, productWrapper);
                  else
                  {
                     ExporterCacheManager.RailingCache.Add(element.Id);
                     RailingExporter.AddSubElementsToCache(element as Railing);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (RampExporter.IsRamp(element))
               {
                  RampExporter.Export(exporterIFC, element, geomElem, productWrapper);
               }
               else if (IsRebarType(element))
               {
                  RebarExporter.Export(exporterIFC, element, productWrapper);
               }
               else if (element is RebarCoupler)
               {
                  RebarCoupler couplerElem = element as RebarCoupler;
                  RebarCouplerExporter.ExportCoupler(exporterIFC, couplerElem, productWrapper);
               }
               else if (element is RoofBase)
               {
                  RoofBase roofElement = element as RoofBase;
                  RoofExporter.Export(exporterIFC, roofElement, ref geomElem, productWrapper);
               }
               else if (element is SpatialElement)
               {
                  SpatialElement spatialElem = element as SpatialElement;
                  SpatialElementExporter.ExportSpatialElement(exporterIFC, spatialElem, productWrapper);
               }
               else if (IsStairs(element))
               {
                  StairsExporter.Export(exporterIFC, element, geomElem, productWrapper);
               }
               else if (element is TextNote)
               {
                  TextNote textNote = element as TextNote;
                  TextNoteExporter.Export(exporterIFC, textNote, productWrapper);
               }
               else if (element is TopographySurface)
               {
                  TopographySurface topSurface = element as TopographySurface;
                  SiteExporter.ExportTopographySurface(exporterIFC, topSurface, geomElem, productWrapper);
               }
               else if (element is Truss)
               {
                  if (ExporterCacheManager.TrussCache.Contains(element.Id))
                     AssemblyInstanceExporter.ExportTrussElement(exporterIFC, element as Truss, productWrapper);
                  else
                  {
                     ExporterCacheManager.TrussCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else if (element is Wall)
               {
                  Wall wallElem = element as Wall;
                  WallExporter.Export(exporterIFC, wallElem, ref geomElem, productWrapper);
               }
               else if (element is WallSweep)
               {
                  WallSweep wallSweep = element as WallSweep;
                  WallSweepExporter.Export(exporterIFC, wallSweep, geomElem, productWrapper);
               }
               else if (element is Zone)
               {
                  if (ExporterCacheManager.ZoneCache.Contains(element.Id))
                     ZoneExporter.ExportZone(exporterIFC, element as Zone, productWrapper);
                  else
                  {
                     ExporterCacheManager.ZoneCache.Add(element.Id);
                     shouldPreserveParameterCache = true;
                  }
               }
               else
               {
                  string ifcEnumType;
                  IFCExportInfoPair exportType = ExporterUtil.GetProductExportType(exporterIFC, element, out ifcEnumType);

                  // Check the intended IFC entity or type name is in the exclude list specified in the UI
                  IFCEntityType elementClassTypeEnum;
                  if (Enum.TryParse(exportType.ExportInstance.ToString(), out elementClassTypeEnum)
                        || Enum.TryParse(exportType.ExportType.ToString(), out elementClassTypeEnum))
                     if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(elementClassTypeEnum))
                        return;

                  // The intention with the code below is to make this the "generic" element exporter, which would export any Revit element as any IFC instance.
                  // We would then in addition have specialized functions that would convert specific Revit elements to specific IFC instances where extra information
                  // could be gathered from the element.
                  bool exported = false;
                  bool elementIsFabricationPart = element is FabricationPart;
                  if (IsMEPType(element, exportType))
                  {
                     exported = GenericMEPExporter.Export(exporterIFC, element, geomElem, exportType, ifcEnumType, productWrapper);
                  }
                  else if (!elementIsFabricationPart && ExportAsProxy(element, exportType))
                  {
                     // Note that we currently export FaceWalls as proxies, and that FaceWalls are HostObjects, so we need
                     // to have this check before the (element is HostObject check.
                     exported = ProxyElementExporter.Export(exporterIFC, element, geomElem, productWrapper, exportType);
                  }
                  else if (elementIsFabricationPart || (element is HostObject) || (element is DirectShape))
                  {
                     exported = GenericElementExporter.ExportElement(exporterIFC, element, geomElem, productWrapper);
                  }

                  if (exported)
                  {
                     // For ducts and pipes, we will add a IfcRelCoversBldgElements during the end of export.
                     if (element is Duct || element is Pipe)
                        ExporterCacheManager.MEPCache.CoveredElementsCache.Add(element.Id);
                     // For cable trays and conduits, we might create systems during the end of export.
                     if (element is CableTray || element is Conduit)
                        ExporterCacheManager.MEPCache.CableElementsCache.Add(element.Id);
                  }
               }

               if (element.AssemblyInstanceId != ElementId.InvalidElementId)
                  ExporterCacheManager.AssemblyInstanceCache.RegisterElements(element.AssemblyInstanceId, productWrapper);
               if (element.GroupId != ElementId.InvalidElementId)
                  ExporterCacheManager.GroupCache.RegisterElements(element.GroupId, productWrapper);

               st.RollBack();
            }
         }
         finally
         {
            exporterIFC.PopExportState();
            ExporterStateManager.PreserveElementParameterCache(element, shouldPreserveParameterCache);
         }
      }

      /// <summary>
      /// Sets the schema information for the current export options.  This can be overridden.
      /// </summary>
      protected virtual IFCFileModelOptions CreateIFCFileModelOptions(ExporterIFC exporterIFC)
      {
         IFCFileModelOptions modelOptions = new IFCFileModelOptions();
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
         {
            modelOptions.SchemaName = "IFC2x2_FINAL";
         }
         else if (ExporterCacheManager.ExportOptionsCache.ExportAs4)
         {
            modelOptions.SchemaName = "IFC4";
         }
         else if (ExporterCacheManager.ExportOptionsCache.ExportAs4x3)
         {
            modelOptions.SchemaName = "IFC4x3";   // Temporary until the ODA Toolkit version used supports the final version
         }
         else
         {
            // We leave IFC2x3 as default until IFC4 is finalized and generally supported across platforms.
            modelOptions.SchemaName = "IFC2x3";
         }
         return modelOptions;
      }

      /// <summary>
      /// Sets the lists of property sets to be exported.  This can be overriden.
      /// </summary>
      protected virtual void InitializePropertySets()
      {
         ExporterInitializer.InitPropertySets();
      }

      /// <summary>
      /// Sets the lists of quantities to be exported.  This can be overriden.
      /// </summary>
      protected virtual void InitializeQuantities(IFCVersion fileVersion)
      {
         ExporterInitializer.InitQuantities(m_QuantitiesToExport, ExporterCacheManager.ExportOptionsCache.ExportBaseQuantities);
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      private void BeginExport(ExporterIFC exporterIFC, View filterView)
      {
         statusBar = RevitStatusBar.Create();

         ElementFilteringUtil.InitCategoryVisibilityCache();
         NamingUtil.InitNameIncrNumberCache();

         string writeIFCExportedElementsVar = Environment.GetEnvironmentVariable("WriteIFCExportedElements");
         if (writeIFCExportedElementsVar != null && writeIFCExportedElementsVar.Length > 0)
         {
            m_Writer = new StreamWriter(@"c:\ifc-output-filters.txt");
         }

         // cache options
         ExportOptionsCache exportOptionsCache = ExportOptionsCache.Create(exporterIFC, filterView);
         ExporterCacheManager.ExportOptionsCache = exportOptionsCache;

         IFCFileModelOptions modelOptions = CreateIFCFileModelOptions(exporterIFC);

         m_IfcFile = IFCFile.Create(modelOptions);
         exporterIFC.SetFile(m_IfcFile);
      }

      private bool ExportBuilding(IList<Level> allLevels)
      {
         foreach (Level level in allLevels)
         {
            if (LevelUtil.IsBuildingStory(level))
               return true;
         }
         return false;
      }

      private void BeginHostDocumentExport(ExporterIFC exporterIFC, Document document)
      {
         ExporterCacheManager.ExportOptionsCache.UpdateForDocument(exporterIFC, document, null);

         // Set language.
         Application app = document.Application;
         string pathName = document.PathName;
         LanguageType langType = LanguageType.Unknown;
         if (!string.IsNullOrEmpty(pathName))
         {
            try
            {
               BasicFileInfo basicFileInfo = BasicFileInfo.Extract(pathName);
               if (basicFileInfo != null)
                  langType = basicFileInfo.LanguageWhenSaved;
            }
            catch
            {
            }
         }
         if (langType == LanguageType.Unknown)
            langType = app.Language;
         ExporterCacheManager.LanguageType = langType;

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle applicationHandle = CreateApplicationInformation(file, document);

         CreateGlobalCartesianOrigin(exporterIFC);
         CreateGlobalDirection(exporterIFC);
         CreateGlobalDirection2D(exporterIFC);

         // Initialize common properties before creating any rooted entities.
         InitializePropertySets();
         InitializeQuantities(ExporterCacheManager.ExportOptionsCache.FileVersion);

         CreateProject(exporterIFC, document, applicationHandle);

         BeginDocumentExportCommon(exporterIFC, document);
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      private void BeginLinkedDocumentExport(ExporterIFC exporterIFC, Document document, string guid)
      {
         ExporterCacheManager.ExportOptionsCache.UpdateForDocument(exporterIFC, document, guid);

         BeginDocumentExportCommon(exporterIFC, document);
      }

      /// <summary>
      /// Initializes the common properties at the beginning of the export process.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      private void BeginDocumentExportCommon(ExporterIFC exporterIFC, Document document)
      {
         // Force GC to avoid finalizer thread blocking and different export time for equal exports
         ForceGarbageCollection();

         IFCFile file = exporterIFC.GetFile();
         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            // create building
            IFCAnyHandle buildingPlacement = CreateBuildingPlacement(file);

            IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;
            
            // create levels
            // Check if there is any level assigned as a building storey, if no at all, model will be exported without Building and BuildingStorey, all containment will be to Site
            IList<Level> allLevels = LevelUtil.FindAllLevels(document);

            bool exportBuilding = ExportBuilding(allLevels);

            // Skip Building if there is no Storey to be exported
            if (exportBuilding)
            {
               CreateBuildingFromProjectInfo(exporterIFC, document, buildingPlacement);

               IList<Element> unassignedBaseLevels = new List<Element>();

               double lengthScale = UnitUtil.ScaleLengthForRevitAPI();

               IFCAnyHandle prevBuildingStorey = null;
               IFCAnyHandle prevPlacement = null;
               double prevHeight = 0.0;
               double prevElev = 0.0;

               int numLevels = allLevels?.Count ?? 0;
               
               // When exporting to IFC 2x3, we have a limited capability to export some Revit view-specific
               // elements, specifically Filled Regions and Text.  However, we do not have the
               // capability to choose which views to export.  As such, we will choose (up to) one DBView per
               // exported level.
               // TODO: Let user choose which view(s) to export.  Ensure that the user know that only one view
               // per level is supported.
               IDictionary<ElementId, View> views = 
                  LevelUtil.FindViewsForLevels(document, ViewType.FloorPlan, allLevels);

               for (int ii = 0; ii < numLevels; ii++)
               {
                  Level level = allLevels[ii];
                  if (level == null)
                     continue;

                  IFCLevelInfo levelInfo = null;

                  if (!LevelUtil.IsBuildingStory(level))
                  {
                     if (prevBuildingStorey == null)
                        unassignedBaseLevels.Add(level);
                     else
                     {
                        levelInfo = IFCLevelInfo.Create(prevBuildingStorey, prevPlacement, prevHeight, prevElev, lengthScale, true);
                        ExporterCacheManager.LevelInfoCache.AddLevelInfo(exporterIFC, level.Id, levelInfo, false);
                     }
                     continue;
                  }

                  if (views.TryGetValue(level.Id, out View view) && view != null)
                  {
                     ExporterCacheManager.DBViewsToExport[view.Id] = level.Id;
                  }

                  double elev = level.ProjectElevation;
                  double height = 0.0;
                  List<ElementId> coincidentLevels = new List<ElementId>();
                  for (int jj = ii + 1; jj < allLevels.Count; jj++)
                  {
                     Level nextLevel = allLevels[jj];
                     if (!LevelUtil.IsBuildingStory(nextLevel))
                        continue;

                     double nextElev = nextLevel.ProjectElevation;
                     if (!MathUtil.IsAlmostEqual(nextElev, elev))
                     {
                        height = nextElev - elev;
                        break;
                     }
                     else if (ExporterCacheManager.ExportOptionsCache.WallAndColumnSplitting)
                        coincidentLevels.Add(nextLevel.Id);
                  }

                  double elevation = UnitUtil.ScaleLength(elev);
                  XYZ orig = new XYZ(0.0, 0.0, elevation);

                  IFCAnyHandle placement = ExporterUtil.CreateLocalPlacement(file, buildingPlacement, orig, null, null);
                  string bsObjectType = NamingUtil.GetObjectTypeOverride(level, null);
                  IFCElementComposition ifcComposition = LevelUtil.GetElementCompositionTypeOverride(level);
                  IFCAnyHandle buildingStorey = IFCInstanceExporter.CreateBuildingStorey(exporterIFC, level, ExporterCacheManager.OwnerHistoryHandle,
                     bsObjectType, placement, ifcComposition, elevation);

                  // Create classification reference when level has classification field name assigned to it
                  ClassificationUtil.CreateClassification(exporterIFC, file, level, buildingStorey);

                  prevBuildingStorey = buildingStorey;
                  prevPlacement = placement;
                  prevHeight = height;
                  prevElev = elev;

                  levelInfo = IFCLevelInfo.Create(buildingStorey, placement, height, elev, lengthScale, true);
                  ExporterCacheManager.LevelInfoCache.AddLevelInfo(exporterIFC, level.Id, levelInfo, true);

                  // if we have coincident levels, add buildingstories for them but use the old handle.
                  for (int jj = 0; jj < coincidentLevels.Count; jj++)
                  {
                     level = allLevels[ii + jj + 1];
                     levelInfo = IFCLevelInfo.Create(buildingStorey, placement, height, elev, lengthScale, true);
                     ExporterCacheManager.LevelInfoCache.AddLevelInfo(exporterIFC, level.Id, levelInfo, true);
                  }

                  ii += coincidentLevels.Count;

                  // We will export element properties, quantities and classifications when we decide to keep the level - we may delete it later.
               }
            }
            transaction.Commit();
         }
      }

      private void GetElementHandles(ICollection<ElementId> ids, ISet<IFCAnyHandle> handles)
      {
         if (ids != null)
         {
            foreach (ElementId id in ids)
            {
               IFCAnyHandle handle = ExporterCacheManager.ElementToHandleCache.Find(id);
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
                  handles.Add(handle);
            }
         }
      }

      private static IFCAnyHandle CreateRelServicesBuildings(IFCAnyHandle buildingHandle, IFCFile file,
         IFCAnyHandle ownerHistory, IFCAnyHandle systemHandle)
      {
         HashSet<IFCAnyHandle> relatedBuildings = new HashSet<IFCAnyHandle>() { buildingHandle };
         string guid = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelServicesBuildings, systemHandle));
         return IFCInstanceExporter.CreateRelServicesBuildings(file, guid,
            ownerHistory, null, null, systemHandle, relatedBuildings);
      }

      private static void UpdateLocalPlacementForElement(IFCAnyHandle elemHnd, IFCFile file,
         IFCAnyHandle containerObjectPlacement, Transform containerInvTrf)
      {
         IFCAnyHandle elemObjectPlacementHnd = IFCAnyHandleUtil.GetObjectPlacement(elemHnd);

         // In the case that the object has no local placement at all.  In that case, create a new default one, and set the object's
         // local placement relative to the containerObjectPlacement.  Note that in this case we are ignoring containerInvTrf
         // entirely, which may not be the right thing to do, but doesn't currently seem to occur in practice.
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(elemObjectPlacementHnd) || !elemObjectPlacementHnd.IsTypeOf("IfcLocalPlacement"))
         {
            IFCAnyHandle relToContainerPlacement =
               ExporterUtil.CreateLocalPlacement(file, containerObjectPlacement, null, null, null);
            IFCAnyHandleUtil.SetAttribute(elemHnd, "ObjectPlacement", relToContainerPlacement);
            return;
         }

         // There are two cases here.
         // 1. We want to update the local placement of the object to be relative to its container without
         // adjusting its global position.  In this case containerInvTrf would be non-null, and we would
         // adjust the relative placement to keep the global position constant.
         // 2. We want to update the local placement of the object to follow any shift of the parent object.
         // In this case containerInvTrf would be null, and we don't update the relative placement.
         Transform newTrf = null;
         if (containerInvTrf != null)
         {
            IFCAnyHandle oldRelativePlacement = IFCAnyHandleUtil.GetInstanceAttribute(elemObjectPlacementHnd, "PlacementRelTo");
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(oldRelativePlacement))
            {
               newTrf = ExporterUtil.GetTransformFromLocalPlacementHnd(elemObjectPlacementHnd);
            }
            else
            {
               Transform originalTotalTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(elemObjectPlacementHnd);
               newTrf = containerInvTrf.Multiply(originalTotalTrf);
            }
         }

         GeometryUtil.SetPlacementRelTo(elemObjectPlacementHnd, containerObjectPlacement);

         if (newTrf == null)
            return;

         IFCAnyHandle newRelativePlacement =
            ExporterUtil.CreateAxis2Placement3D(file, newTrf.Origin, newTrf.BasisZ, newTrf.BasisX);
         GeometryUtil.SetRelativePlacement(elemObjectPlacementHnd, newRelativePlacement);
      }

      private void CreatePresentationLayerAssignments(ExporterIFC exporterIFC, IFCFile file)
      {
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
            return;

         ISet<IFCAnyHandle> assignedRepresentations = new HashSet<IFCAnyHandle>();
         IDictionary<string, ISet<IFCAnyHandle>> combinedPresentationLayerSet =
            new Dictionary<string, ISet<IFCAnyHandle>>();

         foreach (KeyValuePair<string, ICollection<IFCAnyHandle>> presentationLayerSet in ExporterCacheManager.PresentationLayerSetCache)
         {
            ISet<IFCAnyHandle> validHandles = new HashSet<IFCAnyHandle>();
            foreach (IFCAnyHandle handle in presentationLayerSet.Value)
            {
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
               {
                  validHandles.Add(handle);
                  assignedRepresentations.Add(handle);
               }
            }

            if (validHandles.Count == 0)
               continue;

            combinedPresentationLayerSet[presentationLayerSet.Key] = validHandles;
         }

         // Now handle the internal cases.
         IDictionary<string, IList<IFCAnyHandle>> presentationLayerAssignments = exporterIFC.GetPresentationLayerAssignments();
         foreach (KeyValuePair<string, IList<IFCAnyHandle>> presentationLayerAssignment in presentationLayerAssignments)
         {
            // Some of the items may have been deleted, remove them from set.
            ICollection<IFCAnyHandle> newLayeredItemSet = new HashSet<IFCAnyHandle>();
            IList<IFCAnyHandle> initialSet = presentationLayerAssignment.Value;
            foreach (IFCAnyHandle currItem in initialSet)
            {
               if (!IFCAnyHandleUtil.IsNullOrHasNoValue(currItem) && !assignedRepresentations.Contains(currItem))
                  newLayeredItemSet.Add(currItem);
            }

            if (newLayeredItemSet.Count == 0)
               continue;

            string layerName = presentationLayerAssignment.Key;
            ISet<IFCAnyHandle> layeredItemSet;
            if (!combinedPresentationLayerSet.TryGetValue(layerName, out layeredItemSet))
            {
               layeredItemSet = new HashSet<IFCAnyHandle>();
               combinedPresentationLayerSet[layerName] = layeredItemSet;
            }
            layeredItemSet.UnionWith(newLayeredItemSet);
         }

         foreach (KeyValuePair<string, ISet<IFCAnyHandle>> presentationLayerSet in combinedPresentationLayerSet)
         {
            IFCInstanceExporter.CreatePresentationLayerAssignment(file, presentationLayerSet.Key, null, presentationLayerSet.Value, null);
         }
      }

      private void OverrideOneGUID(IFCAnyHandle handle, IDictionary<string, int> indexMap, 
         Document document, ElementId elementId)
      {
         string typeName = handle.TypeName;
         Element element = document.GetElement(elementId);
         if (element == null)
            return;

         if (!indexMap.TryGetValue(typeName, out int index))
            index = 1;

         indexMap[typeName] = index+1;
         string globalId = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(element, "Internal: " + typeName + index.ToString()));
         ExporterUtil.SetGlobalId(handle, globalId);
      }

      private void EndHostDocumentExport(ExporterIFC exporterIFC, Document document)
      {
         EndDocumentExportCommon(exporterIFC, document, false);
      }

      private void EndLinkedDocumentExport(ExporterIFC exporterIFC, Document document,
         Transform linkTrf)
      {
         EndDocumentExportCommon(exporterIFC, document, true);

         bool canUseSitePlacement = 
            ExporterCacheManager.ExportOptionsCache.ExportLinkedFileAs == LinkedFileExportAs.ExportSameProject;
         IFCAnyHandle topHandle = canUseSitePlacement ? 
            ExporterCacheManager.SiteHandle : ExporterCacheManager.BuildingHandle;
         OrientLink(exporterIFC.GetFile(), canUseSitePlacement, 
            IFCAnyHandleUtil.GetObjectPlacement(topHandle), linkTrf);
      }

      /// <summary>
      /// Completes the export process by writing information stored incrementally during export to the file.
      /// </summary>
      /// <param name="exporterIFC">The IFC exporter object.</param>
      /// <param name="document">The document to export.</param>
      /// <param name="exportingLink">True if we are exporting a link.</param>
      private void EndDocumentExportCommon(ExporterIFC exporterIFC, Document document,
         bool exportingLink)
      {
         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            // Relate Ducts and Pipes to their coverings (insulations and linings)
            foreach (ElementId ductOrPipeId in ExporterCacheManager.MEPCache.CoveredElementsCache)
            {
               IFCAnyHandle ductOrPipeHandle = ExporterCacheManager.MEPCache.Find(ductOrPipeId);
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(ductOrPipeHandle))
                  continue;

               HashSet<IFCAnyHandle> coveringHandles = new HashSet<IFCAnyHandle>();

               try
               {
                  ICollection<ElementId> liningIds = InsulationLiningBase.GetLiningIds(document, ductOrPipeId);
                  GetElementHandles(liningIds, coveringHandles);
               }
               catch
               {
               }

               try
               {
                  ICollection<ElementId> insulationIds = InsulationLiningBase.GetInsulationIds(document, ductOrPipeId);
                  GetElementHandles(insulationIds, coveringHandles);
               }
               catch
               {
               }

               if (coveringHandles.Count > 0)
               {
                  string relCoversGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelCoversBldgElements, ductOrPipeHandle));
                  IFCInstanceExporter.CreateRelCoversBldgElements(file, relCoversGuid, ownerHistory, null, null, ductOrPipeHandle, coveringHandles);
               }
            }

            // Relate stair components to stairs
            foreach (KeyValuePair<ElementId, StairRampContainerInfo> stairRamp in ExporterCacheManager.StairRampContainerInfoCache)
            {
               StairRampContainerInfo stairRampInfo = stairRamp.Value;

               IList<IFCAnyHandle> hnds = stairRampInfo.StairOrRampHandles;
               for (int ii = 0; ii < hnds.Count; ii++)
               {
                  IFCAnyHandle hnd = hnds[ii];
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(hnd))
                     continue;

                  IList<IFCAnyHandle> comps = stairRampInfo.Components[ii];
                  if (comps.Count == 0)
                     continue;

                  ExporterUtil.RelateObjects(exporterIFC, null, hnd, comps);
               }
            }

            // create a Default site if we have latitude and longitude information.
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(ExporterCacheManager.SiteHandle))
            {
               using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
               {
                  SiteExporter.ExportDefaultSite(exporterIFC, document, productWrapper);
                  ExporterUtil.ExportRelatedProperties(exporterIFC, document.ProjectInformation, productWrapper);
               }
            }

            ProjectInfo projectInfo = document.ProjectInformation;

            IFCAnyHandle projectHandle = ExporterCacheManager.ProjectHandle;
            IFCAnyHandle siteHandle = ExporterCacheManager.SiteHandle;
            IFCAnyHandle buildingHandle = ExporterCacheManager.BuildingHandle;

            bool projectHasSite = !IFCAnyHandleUtil.IsNullOrHasNoValue(siteHandle);
            bool projectHasBuilding = !IFCAnyHandleUtil.IsNullOrHasNoValue(buildingHandle);

            IFCAnyHandle siteOrbuildingHnd = siteHandle;

            if (!projectHasSite)
            {
               if (!projectHasBuilding)
               {
                  // if at this point the buildingHnd is null, which means that the model does not
                  // have Site nor any Level assigned to the BuildingStorey, create the IfcBuilding 
                  // as the general container for all the elements (should be backward compatible).
                  IFCAnyHandle buildingPlacement = CreateBuildingPlacement(file);
                  buildingHandle = CreateBuildingFromProjectInfo(exporterIFC, document, buildingPlacement);
                  ExporterCacheManager.BuildingHandle = buildingHandle;
                  projectHasBuilding = true;
               }
               siteOrbuildingHnd = buildingHandle;
            }

            // Last chance to create the building handle was just above.
            if (projectHasSite)
            {
               // Don't add the relation if we've already created it, which is if we are
               // exporting a linked file in a federated export while we are sharing the site.
               if (!exportingLink ||
                  ExporterCacheManager.ExportOptionsCache.ExportLinkedFileAs != LinkedFileExportAs.ExportSameSite)
               {
                  ExporterCacheManager.ContainmentCache.AddRelation(projectHandle, siteHandle);
               }

               if (projectHasBuilding)
               {
                  // assoc. site to the building.
                  ExporterCacheManager.ContainmentCache.AddRelation(siteHandle, buildingHandle);

                  IFCAnyHandle buildingPlacement = IFCAnyHandleUtil.GetObjectPlacement(buildingHandle);
                  IFCAnyHandle relPlacement = IFCAnyHandleUtil.GetObjectPlacement(siteHandle);
                  GeometryUtil.SetPlacementRelTo(buildingPlacement, relPlacement);
               }
            }
            else
            {
               // relate building and project if no site
               if (projectHasBuilding)
                  ExporterCacheManager.ContainmentCache.AddRelation(projectHandle, buildingHandle);
            }

            // relate assembly elements to assemblies
            foreach (KeyValuePair<ElementId, AssemblyInstanceInfo> assemblyInfoEntry in ExporterCacheManager.AssemblyInstanceCache)
            {
               AssemblyInstanceInfo assemblyInfo = assemblyInfoEntry.Value;
               if (assemblyInfo == null)
                  continue;

               IFCAnyHandle assemblyInstanceHandle = assemblyInfo.AssemblyInstanceHandle;
               HashSet<IFCAnyHandle> elementHandles = assemblyInfo.ElementHandles;
               if (elementHandles != null && assemblyInstanceHandle != null && elementHandles.Contains(assemblyInstanceHandle))
                  elementHandles.Remove(assemblyInstanceHandle);

               if (assemblyInstanceHandle != null && elementHandles != null && elementHandles.Count != 0)
               {
                  Element assemblyInstance = document.GetElement(assemblyInfoEntry.Key);
                  string guid = GUIDUtil.CreateSubElementGUID(assemblyInstance, (int)IFCAssemblyInstanceSubElements.RelContainedInSpatialStructure);

                  if (IFCAnyHandleUtil.IsSubTypeOf(assemblyInstanceHandle, IFCEntityType.IfcSystem))
                  {
                     IFCInstanceExporter.CreateRelAssignsToGroup(file, guid, ownerHistory, null, null, elementHandles, null, assemblyInstanceHandle);
                  }
                  else
                  {
                     ExporterUtil.RelateObjects(exporterIFC, guid, assemblyInstanceHandle, elementHandles);
                     // Set the PlacementRelTo of assembly elements to assembly instance.
                     IFCAnyHandle assemblyPlacement = IFCAnyHandleUtil.GetObjectPlacement(assemblyInstanceHandle);
                     AssemblyInstanceExporter.SetLocalPlacementsRelativeToAssembly(exporterIFC, assemblyPlacement, elementHandles);
                  }

                  // We don't do this in RegisterAssemblyElement because we want to make sure that the IfcElementAssembly has been created.
                  ExporterCacheManager.ElementsInAssembliesCache.UnionWith(elementHandles);
               }
            }

            // relate group elements to groups
            foreach (KeyValuePair<ElementId, GroupInfo> groupEntry in ExporterCacheManager.GroupCache)
            {
               GroupInfo groupInfo = groupEntry.Value;
               if (groupInfo == null)
                  continue;

               if (groupInfo.GroupHandle != null && groupInfo.ElementHandles != null &&
                   groupInfo.ElementHandles.Count != 0 && groupInfo.GroupType.ExportInstance != IFCEntityType.UnKnown)
               {
                  Element group = document.GetElement(groupEntry.Key);
                  
                  IFCAnyHandle groupHandle = groupInfo.GroupHandle;
                  HashSet<IFCAnyHandle> elementHandles = groupInfo.ElementHandles;


                  if (groupHandle != null)
                  {
                     if (elementHandles?.Contains(groupHandle) ?? false)
                        elementHandles.Remove(groupHandle);

                     if ((elementHandles?.Count ?? 0) > 0)
                  {
                        // Group may be exported as IfcFurniture which contains IfcSystemFurnitureElements, so they need a RelAggregates relationship
                        if (groupEntry.Value.GroupType.ExportInstance == IFCEntityType.IfcFurniture)
                        {
                           string guid = GUIDUtil.GenerateIFCGuidFrom(
                              GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, groupHandle));
                           IFCInstanceExporter.CreateRelAggregates(file, guid, ownerHistory, null, null, groupHandle, elementHandles);
                        }
                        else
                        {
                           string guid = GUIDUtil.CreateSubElementGUID(group, (int)IFCGroupSubElements.RelAssignsToGroup);
                           IFCInstanceExporter.CreateRelAssignsToGroup(file, guid, ownerHistory, null, null, elementHandles, null, groupHandle);
                        }
                     }
                  }
               }
            }

            // Relate levels and products.  This may create new orphaned elements, so deal with those next.
            RelateLevels(exporterIFC, document);

            IFCAnyHandle defContainerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(siteOrbuildingHnd);
            Transform defContainerTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(defContainerObjectPlacement);
            Transform defContainerInvTrf = defContainerTrf.Inverse;

            // create an association between the IfcBuilding and building elements with no other containment.
            HashSet<IFCAnyHandle> buildingElements = RemoveContainedHandlesFromSet(ExporterCacheManager.LevelInfoCache.OrphanedElements);
            buildingElements.UnionWith(exporterIFC.GetRelatedElements());
            if (buildingElements.Count > 0)
            {
               HashSet<IFCAnyHandle> relatedElementSetForSite = new HashSet<IFCAnyHandle>();
               HashSet<IFCAnyHandle> relatedElementSetForBuilding = new HashSet<IFCAnyHandle>();
               // If the object is supposed to be placed directly on Site or Building, change the object placement to be relative to the Site or Building
               foreach (IFCAnyHandle elemHnd in buildingElements)
               {
                  ElementId elementId = ExporterCacheManager.HandleToElementCache.Find(elemHnd);
                  Element elem = document.GetElement(elementId);

                  // if there is override, use the override otherwise use default from site
                  IFCAnyHandle overrideContainer = null;
                  ParameterUtil.OverrideContainmentParameter(exporterIFC, elem, out overrideContainer);

                  bool containerIsSite = projectHasSite;
                  bool containerIsBuilding = projectHasBuilding;

                  IFCAnyHandle containerObjectPlacement = null;
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(overrideContainer))
                  {
                     containerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(overrideContainer);
                     containerIsSite = IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcSite);
                     containerIsBuilding = !containerIsSite &&
                        IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcBuilding);
                  }
                  else
                  {
                     // Default behavior (generally site).
                     containerObjectPlacement = defContainerObjectPlacement;
                  }

                  if (containerIsSite)
                     relatedElementSetForSite.Add(elemHnd);
                  else if (containerIsBuilding)
                     relatedElementSetForBuilding.Add(elemHnd);

                  UpdateLocalPlacementForElement(elemHnd, file, containerObjectPlacement, null);
               }

               if (relatedElementSetForBuilding.Count > 0 && projectHasBuilding)
               {
                  string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelContainedInBuildingSpatialStructure);
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid,
                     ownerHistory, null, null, relatedElementSetForBuilding, buildingHandle);
               }

               if (relatedElementSetForSite.Count > 0 && projectHasSite)
               {
                  string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelContainedInSiteSpatialStructure);
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid,
                     ownerHistory, null, null, relatedElementSetForSite, siteHandle);
               }
            }

            // create an association between the IfcBuilding and spacial elements with no other containment
            // The name "GetRelatedProducts()" is misleading; this only covers spaces.
            HashSet<IFCAnyHandle> buildingSpaces = RemoveContainedHandlesFromSet(ExporterCacheManager.LevelInfoCache.OrphanedSpaces);
            buildingSpaces.UnionWith(exporterIFC.GetRelatedProducts());
            if (buildingSpaces.Count > 0)
            {
               HashSet<IFCAnyHandle> relatedElementSetForBuilding = new HashSet<IFCAnyHandle>();
               HashSet<IFCAnyHandle> relatedElementSetForSite = new HashSet<IFCAnyHandle>();
               foreach (IFCAnyHandle indivSpace in buildingSpaces)
               {
                  bool containerIsSite = projectHasSite;
                  bool containerIsBuilding = projectHasBuilding;

                  // if there is override, use the override otherwise use default from site
                  IFCAnyHandle overrideContainer = null;
                  ParameterUtil.OverrideSpaceContainmentParameter(exporterIFC, document, indivSpace, out overrideContainer);
                  IFCAnyHandle containerObjectPlacement = null;
                  Transform containerInvTrf = null;

                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(overrideContainer))
                  {
                     containerObjectPlacement = IFCAnyHandleUtil.GetObjectPlacement(overrideContainer);
                     Transform containerTrf = ExporterUtil.GetTotalTransformFromLocalPlacement(containerObjectPlacement);
                     containerInvTrf = containerTrf.Inverse;
                     containerIsSite = IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcSite);
                     containerIsBuilding = !containerIsSite &&
                        IFCAnyHandleUtil.IsTypeOf(overrideContainer, IFCEntityType.IfcBuilding);
                  }
                  else
                  {
                     // Default behavior (generally site).
                     containerObjectPlacement = defContainerObjectPlacement;
                     containerInvTrf = defContainerInvTrf;
                  }

                  if (containerIsSite)
                     relatedElementSetForSite.Add(indivSpace);
                  else if (containerIsBuilding)
                     relatedElementSetForBuilding.Add(indivSpace);

                  UpdateLocalPlacementForElement(indivSpace, file, containerObjectPlacement, containerInvTrf);
               }

               if (relatedElementSetForBuilding.Count > 0)
               {
                  ExporterCacheManager.ContainmentCache.AddRelations(buildingHandle, null, relatedElementSetForBuilding);
               }

               if (relatedElementSetForSite.Count > 0)
               {
                  ExporterCacheManager.ContainmentCache.AddRelations(siteHandle, null, relatedElementSetForSite);
               }
            }

            // relate objects in containment cache.
            foreach (KeyValuePair<IFCAnyHandle, HashSet<IFCAnyHandle>> container in ExporterCacheManager.ContainmentCache.Cache)
            {
               if (container.Value.Count() > 0)
               {
                  string relationGUID = ExporterCacheManager.ContainmentCache.GetGUIDForRelation(container.Key);
                  ExporterUtil.RelateObjects(exporterIFC, relationGUID, container.Key, container.Value);
               }
            }

            // These elements are created internally, but we allow custom property sets for them.  Create them here.
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               if (projectHasBuilding)
                  productWrapper.AddBuilding(projectInfo, buildingHandle);
               if (projectInfo != null)
                  ExporterUtil.ExportRelatedProperties(exporterIFC, projectInfo, productWrapper);
            }

            // TODO: combine all of the various material paths and caches; they are confusing and
            // prone to error.

            // create material layer associations
            foreach (KeyValuePair<IFCAnyHandle, ISet<IFCAnyHandle>> materialSetLayerUsage in ExporterCacheManager.MaterialSetUsageCache.Cache)
            {
               if ((materialSetLayerUsage.Value?.Count ?? 0) == 0)
                  continue;

               string hash = ExporterCacheManager.MaterialSetUsageCache.GetHash(materialSetLayerUsage.Key);
               string guid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, hash));
               IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                  null, null, materialSetLayerUsage.Value,
                  materialSetLayerUsage.Key);
            }

            // create material constituent set associations
            foreach (KeyValuePair<IFCAnyHandle, ISet<IFCAnyHandle>> relAssoc in ExporterCacheManager.MaterialConstituentSetCache.Cache)
            {
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(relAssoc.Key))
                  continue;

               ISet<IFCAnyHandle> relatedObjects = ExporterUtil.CleanRefObjects(relAssoc.Value);
               if ((relatedObjects?.Count ?? 0) == 0)
                  continue;

               // TODO_GUID: relAssoc.Value.First() is somewhat stable, as long as the objects
               // always come in the same order and elements aren't deleted.
               string guid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, relAssoc.Value.First()));
               IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                  null, null, relatedObjects, relAssoc.Key);
            }

            // Update the GUIDs for internally created IfcRoot entities, if any.
            // This is a little expensive because of the inverse attribute workaround, so we only
            // do it if any internal handles were created.
            if (ExporterCacheManager.InternallyCreatedRootHandles.Count > 0)
            {
               // Patch IfcOpeningElement, IfcRelVoidsElement, IfcElementQuantity, and
               // IfcRelDefinesByProperties GUIDs created by internal code.  This code is
               // written specifically for this case, but could be easily generalized.

               IDictionary<long, ISet<IFCAnyHandle>> elementIdToHandles = 
                  new SortedDictionary<long, ISet<IFCAnyHandle>>();

               // For some reason, the inverse attribute isn't working.  So we need to get
               // the IfcRelVoidElements and IfcRelDefinesByProperties and effectively create
               // the inverse cache, but only if there were any internally created root handles.

               // First, look at all IfcRelVoidElements.
               IList<IFCAnyHandle> voids = exporterIFC.GetFile().GetInstances(IFCEntityType.IfcRelVoidsElement.ToString(), false);
               foreach (IFCAnyHandle voidHandle in voids)
               {
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(voidHandle))
                     continue;

                  IFCAnyHandle openingHandle = IFCAnyHandleUtil.GetInstanceAttribute(voidHandle, "RelatedOpeningElement");
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(openingHandle))
                     continue;

                  if (!ExporterCacheManager.InternallyCreatedRootHandles.TryGetValue(openingHandle, out ElementId elementId))
                     continue;

                  long elementIdVal = elementId.Value;
                  if (!elementIdToHandles.TryGetValue(elementIdVal, out ISet<IFCAnyHandle> internalHandes))
                  {
                     internalHandes = new SortedSet<IFCAnyHandle>(new BaseRelationsCache.IFCAnyHandleComparer());
                     elementIdToHandles[elementIdVal] = internalHandes;
                  }

                  internalHandes.Add(voidHandle); // IfcRelVoidsElement
               }

               // Second, look at all IfcRelDefinesByProperties.
               IList<IFCAnyHandle> relProperties = exporterIFC.GetFile().GetInstances(IFCEntityType.IfcRelDefinesByProperties.ToString(), false);
               foreach (IFCAnyHandle relPropertyHandle in relProperties)
               {
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(relPropertyHandle))
                     continue;

                  ICollection<IFCAnyHandle> relatedHandles = IFCAnyHandleUtil.GetAggregateInstanceAttribute<List<IFCAnyHandle>>(relPropertyHandle, "RelatedObjects");
                  if ((relatedHandles?.Count ?? 0) == 0)
                     continue;

                  foreach (IFCAnyHandle relatedHandle in relatedHandles)
                  {
                     if (!ExporterCacheManager.InternallyCreatedRootHandles.TryGetValue(relatedHandle, out ElementId elementId))
                        continue;

                     long elementIdVal = elementId.Value;
                     if (!elementIdToHandles.TryGetValue(elementIdVal, out ISet<IFCAnyHandle> internalHandes))
                     {
                        internalHandes = new SortedSet<IFCAnyHandle>(new BaseRelationsCache.IFCAnyHandleComparer());
                        elementIdToHandles[elementIdVal] = internalHandes;
                     }

                     IFCAnyHandle propertyHandle = IFCAnyHandleUtil.GetInstanceAttribute(relPropertyHandle, "RelatingPropertyDefinition");
                     internalHandes.Add(propertyHandle); // IfcQuantitySet
                     internalHandes.Add(relPropertyHandle); // IfcRelDefinesByProperties.
                     break;
                  }
               }

               IDictionary<string, int> indexMap = new Dictionary<string, int>();

               foreach (KeyValuePair<long, ISet<IFCAnyHandle>> handleSets in elementIdToHandles)
               {
                  ElementId elementId = new ElementId(handleSets.Key);
                  foreach (IFCAnyHandle handle in handleSets.Value)
                  {
                     OverrideOneGUID(handle, indexMap, document, elementId);
                  }
               }
            }

            // For some older Revit files, we may have the same material name used for multiple
            // elements.  Without doing a significant amount of rewrite that likely isn't worth
            // the effort, we can at least create a cache here of used names with indices so that
            // we don't duplicate the GUID while still reusing the name to match the model.
            IDictionary<string, int> UniqueMaterialNameCache = new Dictionary<string, int>();

            // create material associations
            foreach (IFCAnyHandle materialHnd in ExporterCacheManager.MaterialRelationsCache.Keys)
            {
               // In some specific cased the reference object might have been deleted. Clear those from the Type cache first here
               ISet<IFCAnyHandle> materialRelationsHandles = ExporterCacheManager.MaterialRelationsCache.CleanRefObjects(materialHnd);
               if ((materialRelationsHandles?.Count ?? 0) > 0)
               {
                  // Everything but IfcMaterialLayerSet uses the "Name" attribute.
                  string materialName = IFCAnyHandleUtil.GetStringAttribute(materialHnd, "Name");
                  if (materialName == null)
                     materialName = IFCAnyHandleUtil.GetStringAttribute(materialHnd, "LayerSetName");

                  string guidHash = materialName;
                  if (UniqueMaterialNameCache.TryGetValue(guidHash, out int index))
                  {
                     UniqueMaterialNameCache[guidHash]++;
                     // In theory this could be a duplicate material name, but really highly
                     // unlikely.
                     guidHash += " GUID Copy: " + index.ToString();
                  }
                  else
                  {
                     UniqueMaterialNameCache[guidHash] = 2;
                  }

                  string guid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesMaterial, guidHash));
                  IFCInstanceExporter.CreateRelAssociatesMaterial(file, guid, ownerHistory,
                     null, null, materialRelationsHandles, materialHnd);
               }
            }

            // create type relations
            foreach (IFCAnyHandle typeObj in ExporterCacheManager.TypeRelationsCache.Keys)
            {
               // In some specific cased the reference object might have been deleted. Clear those from the Type cache first here
               ISet<IFCAnyHandle> typeRelCache = ExporterCacheManager.TypeRelationsCache.CleanRefObjects(typeObj);
               if ((typeRelCache?.Count ?? 0) > 0)
               {
                  string guid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelDefinesByType, typeObj));
                  IFCInstanceExporter.CreateRelDefinesByType(file, guid, ownerHistory, 
                     null, null, typeRelCache, typeObj);
               }
            }

            // Create property set relations.
            ExporterCacheManager.CreatedInternalPropertySets.CreateRelations(file);

            ExporterCacheManager.CreatedSpecialPropertySets.CreateRelations(file);

            // Create type property relations.
            foreach (TypePropertyInfo typePropertyInfo in ExporterCacheManager.TypePropertyInfoCache.Values)
            {
               if (typePropertyInfo.AssignedToType)
                  continue;

               ICollection<IFCAnyHandle> propertySets = typePropertyInfo.PropertySets;
               ISet<IFCAnyHandle> elements = typePropertyInfo.Elements;

               if (elements.Count == 0)
                  continue;

               foreach (IFCAnyHandle propertySet in propertySets)
               {
                  try
                  {
                     ExporterUtil.CreateRelDefinesByProperties(file,
                        ownerHistory, null, null, elements, propertySet);
                  }
                  catch
                  {
                  }
               }
            }

            // create space boundaries
            foreach (SpaceBoundary boundary in ExporterCacheManager.SpaceBoundaryCache)
            {
               SpatialElementExporter.ProcessIFCSpaceBoundary(exporterIFC, boundary, file);
            }

            // create wall/wall connectivity objects
            if (ExporterCacheManager.WallConnectionDataCache.Count > 0 && !ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               IList<IDictionary<ElementId, IFCAnyHandle>> hostObjects = exporterIFC.GetHostObjects();
               List<int> relatingPriorities = new List<int>();
               List<int> relatedPriorities = new List<int>();

               string ifcElementEntityType = IFCEntityType.IfcElement.ToString();

               foreach (WallConnectionData wallConnectionData in ExporterCacheManager.WallConnectionDataCache)
               {
                  foreach (IDictionary<ElementId, IFCAnyHandle> mapForLevel in hostObjects)
                  {
                     IFCAnyHandle wallElementHandle, otherElementHandle;
                     if (!mapForLevel.TryGetValue(wallConnectionData.FirstId, out wallElementHandle))
                        continue;
                     if (!mapForLevel.TryGetValue(wallConnectionData.SecondId, out otherElementHandle))
                        continue;

                     if (!wallElementHandle.IsSubTypeOf(ifcElementEntityType) ||
                        !otherElementHandle.IsSubTypeOf(ifcElementEntityType))
                        continue;

                     // NOTE: Definition of RelConnectsPathElements has the connection information reversed
                     // with respect to the order of the paths.
                     string connectionName = ExporterUtil.GetGlobalId(wallElementHandle) + "|" + 
                        ExporterUtil.GetGlobalId(otherElementHandle);
                     string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelConnectsPathElements,
                           connectionName + ":" + 
                           wallConnectionData.SecondConnectionType.ToString() + ":" +
                           wallConnectionData.FirstConnectionType.ToString()));
                     const string connectionType = "Structural";   // Assigned as Description
                     IFCInstanceExporter.CreateRelConnectsPathElements(file, relGuid, ownerHistory,
                        connectionName, connectionType, wallConnectionData.ConnectionGeometry, 
                        wallElementHandle, otherElementHandle, relatingPriorities,
                        relatedPriorities, wallConnectionData.SecondConnectionType, 
                        wallConnectionData.FirstConnectionType);
                  }
               }
            }

            // create Zones and groups of Zones.
            {
               // Collect zone group names as we go.  We will limit a zone to be only in one group.
               IDictionary<string, ISet<IFCAnyHandle>> zoneGroups = new Dictionary<string, ISet<IFCAnyHandle>>();

               string relAssignsToGroupName = "Spatial Zone Assignment";
               foreach (KeyValuePair<string, ZoneInfo> zone in ExporterCacheManager.ZoneInfoCache)
               {
                  ZoneInfo zoneInfo = zone.Value;
                  if (zoneInfo == null)
                     continue;

                  string zoneName = zone.Key;
                  string zoneGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcZone, zone.Key));
                  IFCAnyHandle zoneHandle = IFCInstanceExporter.CreateZone(file, zoneGuid, ownerHistory,
                      zoneName, zoneInfo.Description, zoneInfo.ObjectType, zoneInfo.LongName);
                  string groupGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, zoneName, zoneHandle));
                  IFCInstanceExporter.CreateRelAssignsToGroup(file, groupGuid, ownerHistory,
                      relAssignsToGroupName, null, zoneInfo.RoomHandles, null, zoneHandle);

                  HashSet<IFCAnyHandle> zoneHnds = new HashSet<IFCAnyHandle>() { zoneHandle };
                  
                  foreach (KeyValuePair<string, IFCAnyHandle> classificationReference in zoneInfo.ClassificationReferences)
                  {
                     string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification,
                        classificationReference.Key, zoneHandle));
                     ExporterCacheManager.ClassificationCache.AddRelation(classificationReference.Value, 
                        relGuid, classificationReference.Key, null, zoneHnds);
                  }

                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(zoneInfo.ZoneCommonProperySetHandle))
                  {
                     ExporterUtil.CreateRelDefinesByProperties(file,
                         ownerHistory, null, null, zoneHnds, zoneInfo.ZoneCommonProperySetHandle);
                  }

                  string groupName = zoneInfo.GroupName;
                  if (!string.IsNullOrWhiteSpace(groupName))
                  {
                     ISet<IFCAnyHandle> currentGroup = null;
                     if (!zoneGroups.TryGetValue(groupName, out currentGroup))
                     {
                        currentGroup = new HashSet<IFCAnyHandle>();
                        zoneGroups.Add(groupName, currentGroup);
                     }
                     currentGroup.Add(zoneHandle);
                  }
               }

               // Create RelAssociatesClassifications.
               foreach (var relAssociatesInfo in ExporterCacheManager.ClassificationCache.ClassificationRelations)
               {
                  if (IFCAnyHandleUtil.IsNullOrHasNoValue(relAssociatesInfo.Key))
                     continue;

                  IFCInstanceExporter.CreateRelAssociatesClassification(file,
                     relAssociatesInfo.Value.GlobalId, ownerHistory, 
                     relAssociatesInfo.Value.Name,
                     relAssociatesInfo.Value.Description,
                     relAssociatesInfo.Value.RelatedObjects, 
                     relAssociatesInfo.Key);
               }

               // now create any zone groups.
               string relAssignsToZoneGroupName = "Zone Group Assignment";
               foreach (KeyValuePair<string, ISet<IFCAnyHandle>> zoneGroup in zoneGroups)
               {
                  string zoneGroupGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcGroup, zoneGroup.Key));
                  IFCAnyHandle zoneGroupHandle = IFCInstanceExporter.CreateGroup(file,
                     zoneGroupGuid, ownerHistory, zoneGroup.Key, null, null);
                  string zoneGroupRelAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, zoneGroup.Key));
                  IFCInstanceExporter.CreateRelAssignsToGroup(file, zoneGroupRelAssignsGuid, 
                     ownerHistory, relAssignsToZoneGroupName, null, zoneGroup.Value, null, 
                     zoneGroupHandle);
               }
            }

            // create Space Occupants
            {
               foreach (string spaceOccupantName in ExporterCacheManager.SpaceOccupantInfoCache.Keys)
               {
                  SpaceOccupantInfo spaceOccupantInfo = ExporterCacheManager.SpaceOccupantInfoCache.Find(spaceOccupantName);
                  if (spaceOccupantInfo != null)
                  {
                     IFCAnyHandle person = IFCInstanceExporter.CreatePerson(file, null, spaceOccupantName, null, null, null, null, null, null);
                     string occupantGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcOccupant, spaceOccupantName));
                     IFCAnyHandle spaceOccupantHandle = IFCInstanceExporter.CreateOccupant(file,
                        occupantGuid, ownerHistory, null, null, spaceOccupantName, person, 
                        IFCOccupantType.NotDefined);

                     string relOccupiesSpacesGuid = GUIDUtil.GenerateIFCGuidFrom(
                        GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelOccupiesSpaces, spaceOccupantHandle));
                     IFCInstanceExporter.CreateRelOccupiesSpaces(file, relOccupiesSpacesGuid, 
                        ownerHistory, null, null, spaceOccupantInfo.RoomHandles, null, 
                        spaceOccupantHandle, null);

                     HashSet<IFCAnyHandle> spaceOccupantHandles = 
                        new HashSet<IFCAnyHandle>() { spaceOccupantHandle };

                     foreach (KeyValuePair<string, IFCAnyHandle> classificationReference in spaceOccupantInfo.ClassificationReferences)
                     {
                        string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                           GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification,
                           classificationReference.Key, spaceOccupantHandle));
                        ExporterCacheManager.ClassificationCache.AddRelation(classificationReference.Value,
                           relGuid, classificationReference.Key, null, spaceOccupantHandles);
                     }

                     if (spaceOccupantInfo.SpaceOccupantProperySetHandle != null && spaceOccupantInfo.SpaceOccupantProperySetHandle.HasValue)
                     {
                        ExporterUtil.CreateRelDefinesByProperties(file, 
                           ownerHistory, null, null, spaceOccupantHandles, spaceOccupantInfo.SpaceOccupantProperySetHandle);
                     }
                  }
               }
            }


            ExportCachedSystem(exporterIFC, document, file, ExporterCacheManager.SystemsCache.BuiltInSystemsCache, ownerHistory, buildingHandle, projectHasBuilding, false);
            ExportCachedSystem(exporterIFC, document, file, ExporterCacheManager.SystemsCache.ElectricalSystemsCache, ownerHistory, buildingHandle, projectHasBuilding, true);
            ExportCableTraySystem(document, file, ExporterCacheManager.MEPCache.CableElementsCache, ownerHistory, buildingHandle, projectHasBuilding);

            // Add presentation layer assignments - this is in addition to those created internally.
            // Any representation in this list will override any internal assignment.
            CreatePresentationLayerAssignments(exporterIFC, file);

            // Add door/window openings.
            ExporterCacheManager.DoorWindowDelayedOpeningCreatorCache.ExecuteCreators(exporterIFC, document);

            foreach (SpaceInfo spaceInfo in ExporterCacheManager.SpaceInfoCache.SpaceInfos.Values)
            {
               if (spaceInfo.RelatedElements.Count > 0)
               {
                  string relContainedGuid = GUIDUtil.GenerateIFCGuidFrom(
                     GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelContainedInSpatialStructure,
                     spaceInfo.SpaceHandle));
                  IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, relContainedGuid, ownerHistory,
                      null, null, spaceInfo.RelatedElements, spaceInfo.SpaceHandle);
               }
            }

            // Удалить дескрипторы, помеченные для удаления
            foreach (IFCAnyHandle handleToDel in ExporterCacheManager.HandleToDeleteCache)
            {
               handleToDel.Delete();
            }

            // Возможно изменение элементов со значениями GUID.
            if (ExporterCacheManager.GUIDsToStoreCache.Count > 0 &&
               ExporterUtil.ExportingHostModel())
            {
               using (SubTransaction st = new SubTransaction(document))
               {
                  st.Start();
                  foreach (KeyValuePair<KeyValuePair<ElementId, BuiltInParameter>, string> elementAndGUID in ExporterCacheManager.GUIDsToStoreCache)
                  {
                     Element element = document.GetElement(elementAndGUID.Key.Key);
                     if (element == null || elementAndGUID.Key.Value == BuiltInParameter.INVALID || elementAndGUID.Value == null)
                        continue;

                     ParameterUtil.SetStringParameter(element, elementAndGUID.Key.Value, elementAndGUID.Value);
                  }
                  st.Commit();
               }
            }

            // Экспорт свойств материала
            if (ExporterCacheManager.ExportOptionsCache.PropertySetOptions.ExportMaterialPsets)
               MaterialPropertiesUtil.ExportMaterialProperties(file, exporterIFC);

            // Разрешить собственному коду удалить некоторые неиспользуемые дескрипторы и очистить внутренний кэш.
            ExporterIFCUtils.EndExportInternal(exporterIFC);
            transaction.Commit();
         }
      }

      private class IFCFileDocumentInfo
      {
         public string VersionGUIDString { get; private set; } = null;

         public int NumberOfSaves { get; private set; } = 0;

         public string ProjectNumber { get; private set; } = string.Empty;

         public string ProjectName { get; private set; } = string.Empty;

         public string ProjectStatus { get; private set; } = string.Empty;

         public string VersionName { get; private set; } = string.Empty;

         public string VersionBuild { get; private set; } = string.Empty;

         public string VersionBuildName { get; private set; } = string.Empty;

         public IFCFileDocumentInfo(Document document)
         {
            if (document == null)
               return;

            ProjectInfo projectInfo = document.ProjectInformation;
            DocumentVersion documentVersion = Document.GetDocumentVersion(document);
            Application application = document?.Application;

            ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;

            VersionGUIDString = documentVersion?.VersionGUID.ToString() ?? string.Empty;
            NumberOfSaves = documentVersion?.NumberOfSaves ?? 0;

            ProjectNumber = projectInfo?.Number ?? string.Empty;
            ProjectName = projectInfo?.Name ?? exportOptionsCache.FileName;
            ProjectStatus = projectInfo?.Status ?? string.Empty;

            VersionName = application?.VersionName;
            VersionBuild = application?.VersionBuild;

            try
            {
               string productName = System.Diagnostics.FileVersionInfo.GetVersionInfo(application?.GetType().Assembly.Location).ProductName;
               VersionBuildName = productName + " " + VersionBuild;
            }
            catch
            { }
         }
      }

      private void OrientLink(IFCFile file, bool canUseSitePlacement, 
         IFCAnyHandle buildingOrSitePlacement, Transform linkTrf)
      {
         // При экспорте Link относительное положение экземпляра Link в модели необходимо преобразовать с помощью
         // смещение от преобразования сайта основной модели
         SiteTransformBasis transformBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
         bool useSitePlacement = canUseSitePlacement && (transformBasis != SiteTransformBasis.Internal && 
            transformBasis != SiteTransformBasis.InternalInTN);
         bool useRotation = transformBasis == SiteTransformBasis.InternalInTN ||
            transformBasis == SiteTransformBasis.ProjectInTN || 
            transformBasis == SiteTransformBasis.Shared || 
            transformBasis == SiteTransformBasis.Site;
         Transform sitePl = 
            new Transform(CoordReferenceInfo.MainModelCoordReferenceOffset ?? Transform.Identity);

         XYZ siteOffset = useSitePlacement ? sitePl.Origin : XYZ.Zero;
         if (useRotation)
         {
            // Для тех, кто ориентирован в TN, необходимо вращение, чтобы вычислить правильное смещение в ориентации TN.
            Transform rotationTrfAtInternal = Transform.CreateRotationAtPoint(XYZ.BasisZ, CoordReferenceInfo.MainModelTNAngle, XYZ.Zero);
            siteOffset += rotationTrfAtInternal.OfPoint(linkTrf.Origin);
         }
         else
         {
            siteOffset += linkTrf.Origin;
         }
         sitePl.Origin = XYZ.Zero;
         linkTrf.Origin = XYZ.Zero;
         Transform linkTotTrf = sitePl.Multiply(linkTrf);
         linkTotTrf.Origin = siteOffset;

         IFCAnyHandle relativePlacement = ExporterUtil.CreateAxis2Placement3D(file, 
            UnitUtil.ScaleLength(linkTotTrf.Origin), linkTotTrf.BasisZ, linkTotTrf.BasisX);

         // Обратите внимание, что здесь мы перезаписываем это для последующих записей, что затирает
         // исходное размещение, поэтому после этого дескриптор IfcBuilding вызывает подозрение без
         // явная очистка.
         GeometryUtil.SetRelativePlacement(buildingOrSitePlacement, relativePlacement);
      }

      /*******************************************************************/
      /////////////////////////////////////////////////////////////////////
      /// <summary>
      /// Запиcать файл IFC после создания всех объектов. 
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="document">Документ для экспорта.</param>
      /////////////////////////////////////////////////////////////////////
      /*******************************************************************/
      private void WriteIFCFile(IFCFile file, IFCFileDocumentInfo ifcFileDocumentInfo)
      {
         if (ifcFileDocumentInfo == null)
            ifcFileDocumentInfo = new IFCFileDocumentInfo(null);

         using (IFCTransaction transaction = new IFCTransaction(file))
         {
            //********************
            // создаем заголовок
            //********************
            ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;

            string coordinationView = null;
            if (exportOptionsCache.ExportAsCoordinationView2)
               coordinationView = "CoordinationView_V2.0";
            else
               coordinationView = "CoordinationView";

            List<string> descriptions = new List<string>();
            if (ExporterCacheManager.ExportOptionsCache.ExportAs2x2)
            {
               descriptions.Add("IFC2X_PLATFORM");
            }
            else if (ExporterCacheManager.ExportOptionsCache.ExportAs4ReferenceView)
            {
               if (ExporterCacheManager.ExportOptionsCache.FileVersion == IFCVersion.IFCSG)
                  descriptions.Add("ViewDefinition [ReferenceView_V1.2, IFC-SG]");
               else
                  descriptions.Add("ViewDefinition [ReferenceView_V1.2]");
            }
            else if (ExporterCacheManager.ExportOptionsCache.ExportAs4DesignTransferView)
            {
               descriptions.Add("ViewDefinition [DesignTransferView_V1.0]");   // Предварительное название, поскольку стандарт еще не выпущен полностью.
            }
            else
            {
               string currentLine;
               if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable)
               {
                  currentLine = string.Format("ViewDefinition [{0}]",
                     "COBie2.4DesignDeliverable");
               }
               else
               {
                  currentLine = string.Format("ViewDefinition [{0}{1}]",
                     coordinationView,
                     exportOptionsCache.ExportBaseQuantities ? ", QuantityTakeOffAddOnView" : "");
               }

               descriptions.Add(currentLine);

            } // ****** HEADER *******


            string versionLine = string.Format("RevitIdentifiers [VersionGUID: {0}, NumberOfSaves: {1}]",
               ifcFileDocumentInfo.VersionGUIDString, ifcFileDocumentInfo.NumberOfSaves);

            descriptions.Add(versionLine);
           
            if (!string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.ExcludeFilter))
            {
               descriptions.Add("Options [Excluded Entities: " + ExporterCacheManager.ExportOptionsCache.ExcludeFilter + "]");
            }

            string projectNumber = ifcFileDocumentInfo?.ProjectNumber ?? string.Empty;
            string projectName = ifcFileDocumentInfo?.ProjectName ?? string.Empty;
            string projectStatus = ifcFileDocumentInfo?.ProjectStatus ?? string.Empty;

            IFCAnyHandle project = ExporterCacheManager.ProjectHandle;
            if (!IFCAnyHandleUtil.IsNullOrHasNoValue(project))
               IFCAnyHandleUtil.UpdateProject(project, projectNumber, projectName, projectStatus);

            IFCInstanceExporter.CreateFileSchema(file);

            // Получите сохраненную информацию заголовка файла из пользовательского интерфейса и используйте ее для экспорта.
            IFCFileHeaderItem fHItem = ExporterCacheManager.ExportOptionsCache.FileHeaderItem;

            /*********************************************************************************/

            ///////////////////
            // SGIMAN COMMENT 
            ///////////////////
            List<string> comment = new List<string>();
            fHItem.FileComment = "/* --- Header File Comment (fHItem.FileComment) --- */";
            comment.Add(fHItem.FileComment);

            /*********************************************************************************/

            // Добавьте информацию в описание файла (например, требование обмена), назначенную в пользовательском интерфейсе.
            if (fHItem.FileDescriptions.Count > 0)
               descriptions.AddRange(fHItem.FileDescriptions);
          
             IFCInstanceExporter.CreateFileDescription(file, descriptions);

            List<string> author = new List<string>();
            
            // --- FIX ---
            fHItem.AuthorName = "sgiman";                               // ****** SGIMAN ******
            fHItem.AuthorEmail = "sgimancs@gmail.com";                  // ****** SGIMAN ******
            author.Add(fHItem.AuthorName);
            author.Add(fHItem.AuthorEmail);
            
            /* original */
            /*
            if (string.IsNullOrEmpty(fHItem.AuthorName) == false)
            {
               author.Add(fHItem.AuthorName);               
               if (string.IsNullOrEmpty(fHItem.AuthorEmail) == false)
                   author.Add(fHItem.AuthorEmail);
               }
               else
                  author.Add(string.Empty);
            */

            /*********************************************************************************/
            ///////////////////////////////////////////////////////////////////////////////////

            List<string> organization = new List<string>();
            fHItem.Organization = "SGIMAN CREATIVE STUDIO & UNAVA SOFT";    // ****** SGIMAN ******
            if (string.IsNullOrEmpty(fHItem.Organization) == false)
               organization.Add(fHItem.Organization);
            else
               organization.Add(string.Empty);

            ///////////////////////////////////////////////////////////////////////////////////
            /*********************************************************************************/

            LanguageType langType = ExporterCacheManager.LanguageType;
            string languageExtension = GetLanguageExtension(langType);
            string versionBuildName = ifcFileDocumentInfo.VersionBuildName;
            string versionInfos = versionBuildName + languageExtension + " - " +
               ExporterCacheManager.ExportOptionsCache.ExporterVersion;

            if (fHItem.Authorization == null)
               fHItem.Authorization = string.Empty;

            IFCInstanceExporter.CreateFileName(file, projectNumber, author, organization,
               ifcFileDocumentInfo.VersionName, versionInfos, fHItem.Authorization);

            transaction.Commit();

            IFCFileWriteOptions writeOptions = new IFCFileWriteOptions()
            {
               FileName = exportOptionsCache.FileName,
               FileFormat = exportOptionsCache.IFCFileFormat
            };

            // Повторно используйте почти всю приведенную выше информацию, чтобы записать дополнительные копии файла IFC.
            if (exportOptionsCache.ExportingSeparateLink())
            {
               IFCAnyHandle buildingOrSiteHnd = ExporterCacheManager.BuildingHandle;
               if (IFCAnyHandleUtil.IsNullOrHasNoValue(buildingOrSiteHnd))
               {
                  buildingOrSiteHnd = ExporterCacheManager.SiteHandle;
               }
               IFCAnyHandle buildingOrSitePlacement = IFCAnyHandleUtil.GetObjectPlacement(buildingOrSiteHnd);
               
               int numRevitLinkInstances = exportOptionsCache.GetNumLinkInstanceInfos();
               for (int ii = 0; ii < numRevitLinkInstances; ii++)
               {
                  // При экспорте Link относительное положение экземпляра Link в модели необходимо преобразовать с помощью
                  // смещение от преобразования сайта основной модели
                  Transform linkTrf = new Transform(ExporterCacheManager.ExportOptionsCache.GetUnscaledLinkInstanceTransform(ii));
                  OrientLink(file, true, buildingOrSitePlacement, linkTrf);
                  
                  string linkInstanceFileName = exportOptionsCache.GetLinkInstanceFileName(ii);
                  if (linkInstanceFileName != null)
                     writeOptions.FileName = linkInstanceFileName;

                  file.Write(writeOptions);
               }
            }
            else
            {
               file.Write(writeOptions);
            }

            // Отобразить сообщение пользователю, когда файл IFC был полностью экспортирован.
            statusBar.Set(Resources.IFCExportComplete);

         } // **** using *****

         InfoText(); // ********* !!! SIMAN !!! *********

      } // ******  WriteIFCFile ******


      //***************************
      /////////////////////////////     
      //    SGIMAN IFC CORRECT
      /////////////////////////////     
      //***************************

      //-----------------
      //     Log file 
      //-----------------
      private void InfoText ()
      { 
      /************************* SGIMAN INFO FILE *************************/
      string pathInfo = @"c:\ProgramData\Autodesk\ApplicationPlugins\IFC 2024.bundle\info.txt";

         // Create a file to write to.
         using (StreamWriter sw = File.CreateText(pathInfo))
         {
            sw.WriteLine("********************************************************");
            sw.WriteLine(" EХPORT IFC");
            sw.WriteLine("********************************************************");
            sw.WriteLine(" ORGANIZATION:\tSgiman Creative Studio & Unava Soft ltd");
            sw.WriteLine(" DEVELOPMENT:\tSCS R&D Group");
            sw.WriteLine(" ADDRESS:\t\t08500, Sobornaya 10, Kyiv, Ukraine");
            sw.WriteLine(" EMAIL:\t\t\tsgimancs@gmail.com");
            sw.WriteLine(" PHONE:\t\t\t+380660409266");
            sw.WriteLine(" PERSONS:\t\tSergey Gasanov & Denis Mironchenko");
            sw.WriteLine("********************************************************");

            // Получите сохраненную информацию заголовка файла из пользовательского интерфейса и используйте ее для экспорта.
            IFCFileHeaderItem fHItem = ExporterCacheManager.ExportOptionsCache.FileHeaderItem;

            sw.WriteLine("fHItem.ApplicationName: " + fHItem.ApplicationName);
            sw.WriteLine("fHItem.Organization: " + fHItem.Organization);
            sw.WriteLine("fHItem.VersionNumber: " + fHItem.VersionNumber);
            sw.WriteLine("fHItem.AuthorEmail: " + fHItem.AuthorEmail);
            sw.WriteLine("fHItem.AuthorName: " + fHItem.AuthorName);
            sw.WriteLine("fHItem.FileComment: " + fHItem.FileComment);
            sw.WriteLine("fHItem.SourceFileName: " + fHItem.SourceFileName);

            // Exporter Cache Manager (Get FileName, Get IFCFileFormat)
            ExportOptionsCache exportOptionsCache = ExporterCacheManager.ExportOptionsCache;           
            IFCFileWriteOptions writeOptions = new IFCFileWriteOptions();
            
            var FileName = exportOptionsCache.FileName;
            var IFCFileFormat = exportOptionsCache.IFCFileFormat;

            sw.WriteLine("FileName: " + FileName);
            sw.WriteLine("FileFormat: " + IFCFileFormat);


            /*--------------------
             * File IFC Corrects 
             *------------------*/
            var path = Path.GetDirectoryName(FileName);
            var ext = Path.GetExtension(FileName);
            var name = Path.GetFileNameWithoutExtension(FileName);
            string NewFileName = path + "\\" + name + "_export" + ext;

            sw.WriteLine("NewFileName: " + NewFileName);

            sw.Close();

            CorrectIFCFile(FileName, NewFileName);

         }

      } // ****** InfoText ******

      /********************
      *  Correct IFC File 
      *********************/
      private void CorrectIFCFile(string SourceFileName, string CorrectFileName)
      {

         StreamWriter fcorr = File.CreateText(CorrectFileName);

         // Read file using StreamReader. Reads file line by line
         using (StreamReader fsource = new StreamReader(SourceFileName))
         {
            string ln;
            while ((ln = fsource.ReadLine()) != null)
            {
               if (ln == "HEADER;") 
               {
                  fcorr.WriteLine("HEADER;");
                  fcorr.WriteLine("");
                  fcorr.WriteLine("/********************************************************");
                  fcorr.WriteLine(" EХPORT IFC (CORRECT)");
                  fcorr.WriteLine("********************************************************");
                  fcorr.WriteLine(" ORGANIZATION:\tSgiman Creative Studio & Unava Soft ltd");
                  fcorr.WriteLine(" DEVELOPMENT:\tSCS R&D Group");
                  fcorr.WriteLine(" ADDRESS:\t\t08500, Sobornaya 10, Kyiv, Ukraine");
                  fcorr.WriteLine(" EMAIL:\t\t\tsgimancs@gmail.com");
                  fcorr.WriteLine(" PHONE:\t\t\t+380660409266");
                  fcorr.WriteLine(" PERSONS:\t\tSergey Gasanov & Denis Mironchenko");
                  fcorr.WriteLine("********************************************************/");
                  fcorr.WriteLine("");
               }
               else
                  fcorr.WriteLine(ln);
            }

            fsource.Close();
            fcorr.Close();

            File.Delete(SourceFileName);

         } // ***** using *****

      } // ***** CorrectIFCFile *****
      
      /*********************** END SGIMAN ***********************/



      private string GetLanguageExtension(LanguageType langType)
      {
         switch (langType)
         {
            case LanguageType.English_USA:
               return " (ENU)";
            case LanguageType.German:
               return " (DEU)";
            case LanguageType.Spanish:
               return " (ESP)";
            case LanguageType.French:
               return " (FRA)";
            case LanguageType.Italian:
               return " (ITA)";
            case LanguageType.Dutch:
               return " (NLD)";
            case LanguageType.Chinese_Simplified:
               return " (CHS)";
            case LanguageType.Chinese_Traditional:
               return " (CHT)";
            case LanguageType.Japanese:
               return " (JPN)";
            case LanguageType.Korean:
               return " (KOR)";
            case LanguageType.Russian:
               return " (RUS)";
            case LanguageType.Czech:
               return " (CSY)";
            case LanguageType.Polish:
               return " (PLK)";
            case LanguageType.Hungarian:
               return " (HUN)";
            case LanguageType.Brazilian_Portuguese:
               return " (PTB)";
            case LanguageType.English_GB:
               return " (ENG)";
            default:
               return "";
         }
      }

      private long GetCreationDate(Document document)
      {
         string pathName = document.PathName;

         // Если это не локально сохраненный файл, мы не сможем получить дату создания.
         // Для этого потребуется дополнительная работа, но она очень незначительна.
         DateTime creationTimeUtc = DateTime.Now;
         try
         {
            FileInfo fileInfo = new FileInfo(pathName);
            creationTimeUtc = fileInfo.CreationTimeUtc;
         }
         catch
         {
            creationTimeUtc = DateTime.Now;
         }

         // IfcTimeStamp измеряется в секундах с 01.01.1970. Таким образом, мы делим на 10 000 000
         // (100 нс тактов в секунду) и вычитаем смещение 01.01.1970.
         return creationTimeUtc.ToFileTimeUtc() / 10000000 - 11644473600;
      }

      /// <summary>
      /// Создает информацию о приложении.
      /// </summary>
      /// <param name="file">Файл IFC.</param>
      /// <param name="app">Объект приложения.</param>
      /// <returns>Дескриптор файла IFC.</returns>
      private IFCAnyHandle CreateApplicationInformation(IFCFile file, Document document)
      {
         Application app = document.Application;
         string pathName = document.PathName;
         LanguageType langType = ExporterCacheManager.LanguageType;
         string languageExtension = GetLanguageExtension(langType);
         string productFullName = app.VersionName + languageExtension;
         string productVersion = app.VersionNumber;
         string productIdentifier = "Revit";

         IFCAnyHandle developer = IFCInstanceExporter.CreateOrganization(file, null, productFullName, null, null, null);
         IFCAnyHandle application = IFCInstanceExporter.CreateApplication(file, developer, productVersion, productFullName, productIdentifier);
         return application;
      }

      /// <summary>
      /// Создает информацию о 3D- и 2D-контекстах.
      /// </summary>            
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="doc">Документ предоставляет ProjectLocation.</param>
      /// <returns>Коллекция содержит дескрипторы 3D/2D контекста (не подконтекста) файла IFC.</returns>
      private HashSet<IFCAnyHandle> CreateContextInformation(ExporterIFC exporterIFC, Document doc, out IList<double> directionRatios)
      {
         HashSet<IFCAnyHandle> repContexts = new HashSet<IFCAnyHandle>();

         // Убедитесь, что это значение точности находится в приемлемом диапазоне.
         double initialPrecision = doc.Application.VertexTolerance / 10.0;
         initialPrecision = Math.Min(initialPrecision, 1e-3);
         initialPrecision = Math.Max(initialPrecision, 1e-8);

         double scaledPrecision = UnitUtil.ScaleLength(initialPrecision);
         int exponent = Convert.ToInt32(Math.Log10(scaledPrecision));
         double precision = Math.Pow(10.0, exponent);

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle wcs = null;
         XYZ unscaledOrigin = XYZ.Zero;

         if (ExporterCacheManager.ExportOptionsCache.ExportingSeparateLink())
         {
            if (CoordReferenceInfo.MainModelGeoRefOrWCS != null)
            {
               unscaledOrigin = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin;
               directionRatios = new List<double>(2) { CoordReferenceInfo.MainModelGeoRefOrWCS.BasisY.X, CoordReferenceInfo.MainModelGeoRefOrWCS.BasisY.Y };
            }
            else
               directionRatios = new List<double>(2) { 0.0, 1.0 };

            if (CoordReferenceInfo.CrsInfo.CrsInfoNotSet || ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               // Для IFC2x3 и более ранних версий, или когда информация IfcMapConversion не предоставлена,
               // восточные, северные и ортогональные высоты будут назначены wcs
               XYZ orig = UnitUtil.ScaleLength(unscaledOrigin);
               wcs = ExporterUtil.CreateAxis2Placement3D(file, orig, null, null);
            }
            else
            {
               // Начиная с IFC4 и EPSG имеет значение (будет создано IfcMapConversion), wcs будет (0,0,0)
               wcs = IFCInstanceExporter.CreateAxis2Placement3D(file, ExporterCacheManager.Global3DOriginHandle, null, null);
            }
         }
         else
         {
            // Эти полные вычисления для WCS или информация о преобразовании карты будут использоваться только для основной модели.
            // Модели ссылок будут следовать основной модели

            SiteTransformBasis transformBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
            ProjectLocation projLocation = ExporterCacheManager.SelectedSiteProjectLocation;

            (double eastings, double northings, double orthogonalHeight, double angleTN, double origAngleTN) geoRefInfo =
               OptionsUtil.GeoReferenceInformation(doc, transformBasis, projLocation);

            CoordReferenceInfo.MainModelTNAngle = geoRefInfo.origAngleTN;
            double trueNorthAngleConverted = geoRefInfo.angleTN + Math.PI / 2.0;
            directionRatios = new List<Double>(2) { Math.Cos(trueNorthAngleConverted), Math.Sin(trueNorthAngleConverted) };

            unscaledOrigin = new XYZ(geoRefInfo.eastings, geoRefInfo.northings, geoRefInfo.orthogonalHeight);
            if (string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode) ||
               ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               // Для IFC2x3 и более ранних версий, или когда информация IfcMapConversion не предоставлена,
               // восточные, северные и ортогональные высоты будут назначены wcs
               XYZ orig = UnitUtil.ScaleLength(unscaledOrigin);
               wcs = ExporterUtil.CreateAxis2Placement3D(file, orig, null, null);
            }
            else
            {
               // Начиная с IFC4 и EPSG имеет значение (будет создано IfcMapConversion), wcs будет (0,0,0)
               wcs = IFCInstanceExporter.CreateAxis2Placement3D(file, ExporterCacheManager.Global3DOriginHandle, null, null);
            }
         }

         // Это касается внутреннего случая и общего случая для IFC4+.
         // ПРИМЕЧАНИЕ. Если появятся новые случаи, о них следует рассказать выше.
         if (wcs == null)
         {
            wcs = ExporterUtil.CreateAxis2Placement3D(file, XYZ.Zero, null, null);
         }

         IFCAnyHandle trueNorth = IFCInstanceExporter.CreateDirection(file, directionRatios);
         int dimCount = 3;
         IFCAnyHandle context3D = IFCInstanceExporter.CreateGeometricRepresentationContext(file, null,
             "Model", dimCount, precision, wcs, trueNorth);

         // КоординацияView2.0 требует подконтекстов «Axis», «Body» и «Box».
         // Мы также будем использовать их для обычного экспорта.
         {
            IFCAnyHandle context3DAxis = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "Axis", "Model", context3D, null, IFCGeometricProjection.Graph_View, null);
            IFCAnyHandle context3DBody = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "Body", "Model", context3D, null, IFCGeometricProjection.Model_View, null);
            IFCAnyHandle context3DBox = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "Box", "Model", context3D, null, IFCGeometricProjection.Model_View, null);
            IFCAnyHandle context3DFootPrint = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "FootPrint", "Model", context3D, null, IFCGeometricProjection.Model_View, null);

            ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.Axis, context3DAxis);
            ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.Body, context3DBody);
            ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.Box, context3DBox);
            ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.FootPrint, context3DFootPrint);
         }

         ExporterCacheManager.Set3DContextHandle(exporterIFC, IFCRepresentationIdentifier.None, context3D);
         repContexts.Add(context3D); // В списке только контексты, а не подконтексты.

         // Создайте информацию IFCMapConversion для контекста.
         if (!ExportIFCMapConversion(exporterIFC, doc, context3D, directionRatios))
         {
            // Сохраните информацию о преобразовании для WCS основной модели, чтобы использовать ее позже для экспорта файлов ссылок.
            Transform wcsTr = Transform.Identity;
            wcsTr.Origin = unscaledOrigin;
            wcsTr.BasisY = new XYZ(directionRatios[0], directionRatios[1], 0.0);
            wcsTr.BasisZ = new XYZ(0.0, 0.0, 1.0);
            wcsTr.BasisX = wcsTr.BasisY.CrossProduct(wcsTr.BasisZ);
            CoordReferenceInfo.MainModelGeoRefOrWCS = wcsTr;
         }

         if (ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
         {
            IFCAnyHandle context2DHandle = IFCInstanceExporter.CreateGeometricRepresentationContext(file,
                null, "Plan", dimCount, precision, wcs, trueNorth);

            IFCAnyHandle context2D = IFCInstanceExporter.CreateGeometricRepresentationSubContext(file,
                "Annotation", "Plan", context2DHandle, 0.01, IFCGeometricProjection.Plan_View, null);

            ExporterCacheManager.Set2DContextHandle(exporterIFC, IFCRepresentationIdentifier.Annotation, context2D);
            ExporterCacheManager.Set2DContextHandle(exporterIFC, IFCRepresentationIdentifier.None, context2D);
            
            repContexts.Add(context2DHandle); // В списке только контексты, а не подконтексты
         }

         return repContexts;
      }

      private void GetCOBieContactInfo(IFCFile file, Document doc)
      {
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3ExtendedFMHandoverView)
         {
            try
            {
               string CObieContactXML = Path.GetDirectoryName(doc.PathName) + @"\" + Path.GetFileNameWithoutExtension(doc.PathName) + @"_COBieContact.xml";
               string category = null, company = null, department = null, organizationCode = null, contactFirstName = null, contactFamilyName = null,
                   postalBox = null, town = null, stateRegion = null, postalCode = null, country = null;

               using (XmlReader reader = XmlReader.Create(CObieContactXML))
               {
                  IList<string> eMailAddressList = new List<string>();
                  IList<string> telNoList = new List<string>();
                  IList<string> addressLines = new List<string>();

                  while (reader.Read())
                  {
                     if (reader.IsStartElement())
                     {
                        while (reader.Read())
                        {
                           switch (reader.Name)
                           {
                              case "Email":
                                 eMailAddressList.Add(reader.ReadString());
                                 break;
                              case "Classification":
                                 category = reader.ReadString();
                                 break;
                              case "Company":
                                 company = reader.ReadString();
                                 break;
                              case "Phone":
                                 telNoList.Add(reader.ReadString());
                                 break;
                              case "Department":
                                 department = reader.ReadString();
                                 break;
                              case "OrganizationCode":
                                 organizationCode = reader.ReadString();
                                 break;
                              case "FirstName":
                                 contactFirstName = reader.ReadString();
                                 break;
                              case "LastName":
                                 contactFamilyName = reader.ReadString();
                                 break;
                              case "Street":
                                 addressLines.Add(reader.ReadString());
                                 break;
                              case "POBox":
                                 postalBox = reader.ReadString();
                                 break;
                              case "Town":
                                 town = reader.ReadString();
                                 break;
                              case "State":
                                 stateRegion = reader.ReadString();
                                 break;
                              case "Zip":
                                 category = reader.ReadString();
                                 break;
                              case "Country":
                                 country = reader.ReadString();
                                 break;
                              case "Contact":
                                 if (reader.IsStartElement()) break;     // Ничего не делайте в начальном теге, обрабатывайте, когда он станет концом
                                 CreateContact(file, category, company, department, organizationCode, contactFirstName,
                                     contactFamilyName, postalBox, town, stateRegion, postalCode, country,
                                     eMailAddressList, telNoList, addressLines);
                                 // reset variables
                                 {
                                    category = null;
                                    company = null;
                                    department = null;
                                    organizationCode = null;
                                    contactFirstName = null;
                                    contactFamilyName = null;
                                    postalBox = null;
                                    town = null;
                                    stateRegion = null;
                                    postalCode = null;
                                    country = null;
                                    eMailAddressList.Clear();
                                    telNoList.Clear();
                                    addressLines.Clear();
                                 }
                                 break;
                              default:
                                 break;
                           }
                        }
                     }
                  }
               }
            }
            catch
            {
               // Не могу найти файл XML, игнорирую весь процесс и продолжаю.
            }
         }
      }

      private void CreateActor(string actorName, IFCAnyHandle clientOrg, IFCAnyHandle projectHandle,
         HashSet<IFCAnyHandle> projectHandles, IFCAnyHandle ownerHistory, IFCFile file)
      {
         string actorGuid = GUIDUtil.GenerateIFCGuidFrom(GUIDUtil.CreateGUIDString(IFCEntityType.IfcActor, 
            actorName, projectHandle));
         IFCAnyHandle actor = IFCInstanceExporter.CreateActor(file, actorGuid, ownerHistory, 
            null, null, null, clientOrg);

         string actorRelAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
            GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToActor, actorName, projectHandle));
         IFCInstanceExporter.CreateRelAssignsToActor(file, actorRelAssignsGuid, ownerHistory, actorName,
            null, projectHandles, null, actor, null);
      }

      /// <summary>
      /// Создает IfcProject.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="doc">Документ предоставляет информацию о владельце.</param>
      /// <param name="application">Дескриптор файла IFC для создания истории владельца.</param>
      private void CreateProject(ExporterIFC exporterIFC, Document doc, IFCAnyHandle application)
      {
         string familyName;
         string givenName;
         List<string> middleNames;
         List<string> prefixTitles;
         List<string> suffixTitles;

         string author = String.Empty;
         ProjectInfo projectInfo = doc.ProjectInformation;
         if (projectInfo != null)
         {
            try
            {
               author = projectInfo.Author;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
               // Если не удалось получить имя автора из информации о проекте,
               // попробуйте позже получить имя пользователя из приложения.
            }
         }

         if (string.IsNullOrEmpty(author))
         {
            author = doc.Application.Username;
         }

         NamingUtil.ParseName(author, out familyName, out givenName, out middleNames, out prefixTitles, out suffixTitles);

         IFCFile file = exporterIFC.GetFile();
         int creationDate = (int)GetCreationDate(doc);
         IFCAnyHandle ownerHistory = null;
         IFCAnyHandle person = null;
         IFCAnyHandle organization = null;

         COBieCompanyInfo cobieCompInfo = ExporterCacheManager.ExportOptionsCache.COBieCompanyInfo;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieCompInfo != null)
         {
            IFCAnyHandle postalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null, null, new List<string>() { cobieCompInfo.StreetAddress },
               null, cobieCompInfo.City, cobieCompInfo.State_Region, cobieCompInfo.PostalCode, cobieCompInfo.Country);
            IFCAnyHandle telecomAddress = IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, new List<string>() { cobieCompInfo.CompanyPhone },
               null, null, new List<string>() { cobieCompInfo.CompanyEmail }, null);

            organization = IFCInstanceExporter.CreateOrganization(file, null, cobieCompInfo.CompanyName, null,
                null, new List<IFCAnyHandle>() { postalAddress, telecomAddress });
            person = IFCInstanceExporter.CreatePerson(file, null, null, null, null, null, null, null, null);
         }
         else
         {
            IFCAnyHandle telecomAddress = GetTelecomAddressFromExtStorage(file);
            IList<IFCAnyHandle> telecomAddresses = null;
            if (telecomAddress != null)
            {
               telecomAddresses = new List<IFCAnyHandle>();
               telecomAddresses.Add(telecomAddress);
            }

            person = IFCInstanceExporter.CreatePerson(file, null, familyName, givenName, middleNames,
                prefixTitles, suffixTitles, null, telecomAddresses);


            //***********************************************************************************************************
            // ------ SGIMAN ------
            // #1  = IFCORGANIZATION($,'Autodesk Revit 2024 (ENU)',$,$,$);
            // #16 = IFCORGANIZATION($,'SGIMAN CREATIVE STUDIO (SCS ltd.)','SCS & UNAVA SOFT ltd.',$,$);
            // ----------------------------------------------------------------------------------------------------------
            
            string organizationName = null;
            string organizationDescription = null;
            if (projectInfo != null)
            {
               try
               {
                  organizationName = "SGIMAN CREATIVE STUDIO (R&D GROUP)";     
                  organizationDescription = "SCS & UNAVA SOFT ltd.";
                  
                  //organizationName = projectInfo.OrganizationName;
                  //organizationDescription = projectInfo.OrganizationDescription;
               }
               catch (Autodesk.Revit.Exceptions.InvalidOperationException)
               {
               }
            }
            //***********************************************************************************************************

            organization = IFCInstanceExporter.CreateOrganization(file, null, organizationName, organizationDescription, null, null);
         }

         IFCAnyHandle owningUser = IFCInstanceExporter.CreatePersonAndOrganization(file, person, organization, null);
         ownerHistory = IFCInstanceExporter.CreateOwnerHistory(file, owningUser, application, null,
            Toolkit.IFCChangeAction.NoChange, null, null, null, creationDate);

         exporterIFC.SetOwnerHistoryHandle(ownerHistory);    // Только для использования собственным кодом.
         ExporterCacheManager.OwnerHistoryHandle = ownerHistory;

         // Получение контактной информации из расширяемого хранилища Revit, созданного инструментом расширения COBie.
         GetCOBieContactInfo(file, doc);

         IFCAnyHandle units = CreateDefaultUnits(exporterIFC, doc);
         IList<double> directionRatios = null;
         HashSet<IFCAnyHandle> repContexts = CreateContextInformation(exporterIFC, doc, out directionRatios);

         string projectName = null;
         string projectLongName = null;
         string projectDescription = null;
         string projectPhase = null;

         COBieProjectInfo cobieProjInfo = ExporterCacheManager.ExportOptionsCache.COBieProjectInfo;
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieProjInfo != null)
         {
            projectName = cobieProjInfo.ProjectName;
            projectDescription = cobieProjInfo.ProjectDescription;
            projectPhase = cobieProjInfo.ProjectPhase;
         }
         else
         {
            // Согласно соглашению с разработчиком IFC, мы получаем IfcProject.Name из ProjectInfo.Number и IfcProject.Longname из ProjectInfo.Name.
            projectName = projectInfo?.Number;
            projectLongName = projectInfo?.Name;

            // Получить описание проекта, если оно указано в информации о проекте.
            projectDescription = (projectInfo != null) ? NamingUtil.GetDescriptionOverride(projectInfo, null) : null;

            if (projectInfo != null)
               if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProject.Phase", out projectPhase) == null)
                  ParameterUtil.GetStringValueFromElement(projectInfo, "Project Phase", out projectPhase);
         }

         string projectGUID = GUIDUtil.CreateProjectLevelGUID(doc, GUIDUtil.ProjectLevelGUIDType.Project);
         IFCAnyHandle projectHandle = IFCInstanceExporter.CreateProject(exporterIFC, projectInfo, projectGUID, ownerHistory,
            projectName, projectDescription, projectLongName, projectPhase, repContexts, units);
         ExporterCacheManager.ProjectHandle = projectHandle;


         if (projectInfo != null)
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               productWrapper.AddProject(projectInfo, projectHandle);
               ExporterUtil.ExportRelatedProperties(exporterIFC, projectInfo, productWrapper);
            }
         }

         if (ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE)
         {
            HashSet<IFCAnyHandle> projectHandles = new HashSet<IFCAnyHandle>() { projectHandle };
            
            string clientName = (projectInfo != null) ? projectInfo.ClientName : string.Empty;
            IFCAnyHandle clientOrg = IFCInstanceExporter.CreateOrganization(file, null, clientName, null, null, null);

            CreateActor("Project Client/Owner", clientOrg, projectHandle, projectHandles, ownerHistory,
               file);
            CreateActor("Project Architect", clientOrg, projectHandle, projectHandles, ownerHistory,
               file);
         }
      }

      private void CreateContact(IFCFile file, string category, string company, string department, string organizationCode, string contactFirstName,
          string contactFamilyName, string postalBox, string town, string stateRegion, string postalCode, string country,
          IList<string> eMailAddressList, IList<string> telNoList, IList<string> addressLines)
      {
         IFCAnyHandle contactTelecomAddress = IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, telNoList, null, null, eMailAddressList, null);
         IFCAnyHandle contactPostalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null, department, addressLines, postalBox, town, stateRegion,
                 postalCode, country);
         IList<IFCAnyHandle> contactAddresses = new List<IFCAnyHandle>();
         contactAddresses.Add(contactTelecomAddress);
         contactAddresses.Add(contactPostalAddress);
         IFCAnyHandle contactPerson = IFCInstanceExporter.CreatePerson(file, null, contactFamilyName, contactFirstName, null,
             null, null, null, contactAddresses);
         IFCAnyHandle contactOrganization = IFCInstanceExporter.CreateOrganization(file, organizationCode, company, null,
             null, null);
         IFCAnyHandle actorRole = IFCInstanceExporter.CreateActorRole(file, "UserDefined", category, null);
         IList<IFCAnyHandle> actorRoles = new List<IFCAnyHandle>();
         actorRoles.Add(actorRole);
         IFCAnyHandle contactEntry = IFCInstanceExporter.CreatePersonAndOrganization(file, contactPerson, contactOrganization, actorRoles);
      }

      private IFCAnyHandle GetTelecomAddressFromExtStorage(IFCFile file)
      {
         IFCFileHeaderItem fHItem = ExporterCacheManager.ExportOptionsCache.FileHeaderItem;
         if (!string.IsNullOrEmpty(fHItem.AuthorEmail))
         {
            IList<string> electronicMailAddress = new List<string>();
            electronicMailAddress.Add(fHItem.AuthorEmail);
            return IFCInstanceExporter.CreateTelecomAddress(file, null, null, null, null, null, null, electronicMailAddress, null);
         }

         return null;
      }

      /// <summary>
      /// Создайте адрес IFC на основе сохраненных данных, полученных пользовательским интерфейсом и сохраненных в расширяемом хранилище.
      /// </summary>
      /// <param name="file"></param>
      /// <param name="document"></param>
      /// <returns>Дескриптор файла IFC.</returns>
      static public IFCAnyHandle CreateIFCAddressFromExtStorage(IFCFile file, Document document)
      {
         IFCAddress savedAddress = new IFCAddress();
         IFCAddressItem savedAddressItem;

         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            if (!savedAddressItem.HasData())
               return null;

            IFCAnyHandle postalAddress;

            // У нас есть адрес, сохраненный в расширяемом хранилище.
            List<string> addressLines = null;
            if (!string.IsNullOrEmpty(savedAddressItem.AddressLine1))
            {
               addressLines = new List<string>();

               addressLines.Add(savedAddressItem.AddressLine1);
               if (!string.IsNullOrEmpty(savedAddressItem.AddressLine2))
                  addressLines.Add(savedAddressItem.AddressLine2);
            }

            IFCAddressType? addressPurpose = null;
            if (!string.IsNullOrEmpty(savedAddressItem.Purpose))
            {
               addressPurpose = IFCAddressType.UserDefined;     // установите это как значение по умолчанию
               if (string.Compare(savedAddressItem.Purpose, "OFFICE", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Office;
               else if (string.Compare(savedAddressItem.Purpose, "SITE", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Site;
               else if (string.Compare(savedAddressItem.Purpose, "HOME", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.Home;
               else if (string.Compare(savedAddressItem.Purpose, "DISTRIBUTIONPOINT", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.DistributionPoint;
               else if (string.Compare(savedAddressItem.Purpose, "USERDEFINED", true) == 0)
                  addressPurpose = Toolkit.IFCAddressType.UserDefined;
            }

            postalAddress = IFCInstanceExporter.CreatePostalAddress(file, addressPurpose, savedAddressItem.Description, savedAddressItem.UserDefinedPurpose,
               savedAddressItem.InternalLocation, addressLines, savedAddressItem.POBox, savedAddressItem.TownOrCity, savedAddressItem.RegionOrState, savedAddressItem.PostalCode,
               savedAddressItem.Country);

            return postalAddress;
         }

         return null;
      }

      /// <summary>
      /// Проверьте, есть ли адресная информация, которая не является пустой и которую необходимо создать для IfcSite.
      /// </summary>
      /// <param name="document">the document</param>
      /// <returns>true если нужно создать адрес для сайта</returns>
      static public bool NeedToCreateAddressForSite(Document document)
      {
         IFCAddress savedAddress = new IFCAddress();
         IFCAddressItem savedAddressItem;
         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            // Верните флажок выбора независимо от того, есть ли в нем данные. Он будет проверен перед созданием почтового адреса позже.
            return savedAddressItem.AssignAddressToSite;
         }
         return false;  // по умолчанию адрес сайта не создается, если он не установлен в пользовательском интерфейсе
      }

      /// <summary>
      /// Проверьте, есть ли адресная информация, которая не является пустой и которую необходимо создать для IfcBuilding.
      /// </summary>
      /// <param name="document">the document</param>
      /// <returns>true если адрес должен быть создан для здания</returns>
      static public bool NeedToCreateAddressForBuilding(Document document)
      {
         IFCAddress savedAddress = new IFCAddress();
         IFCAddressItem savedAddressItem;
         if (savedAddress.GetSavedAddress(document, out savedAddressItem) == true)
         {
            // Верните флажок выбора независимо от того, есть ли в нем данные.
            // Он будет проверен перед созданием почтового адреса позже.
            return savedAddressItem.AssignAddressToBuilding;
         }
         // по умолчанию, когда в пользовательском интерфейсе экспорта нет информации об адресе,
         // поэтому он попытается получить другую информацию из информации о проекте или местоположения.
         return true;   
      }

      /// <summary>
      /// Создает IfcPostalAddress и назначает его файлу.
      /// </summary>
      /// <param name="file">Файл IFC.</param>
      /// <param name="address">Строка адреса.</param>
      /// <param name="town">Строка города.</param>
      /// <returns>Дескриптор файла IFC.</returns>
      static public IFCAnyHandle CreateIFCAddress(IFCFile file, Document document, ProjectInfo projInfo)
      {
         IFCAnyHandle postalAddress = null;
         postalAddress = CreateIFCAddressFromExtStorage(file, document);
         if (postalAddress != null)
            return postalAddress;

         string projectAddress = projInfo != null ? projInfo.Address : string.Empty;
         SiteLocation siteLoc = ExporterCacheManager.SelectedSiteProjectLocation.GetSiteLocation();
         string location = siteLoc != null ? siteLoc.PlaceName : string.Empty;

         if (projectAddress == null)
            projectAddress = string.Empty;
         if (location == null)
            location = string.Empty;

         List<string> parsedAddress = new List<string>();
         string city = string.Empty;
         string state = string.Empty;
         string postCode = string.Empty;
         string country = string.Empty;

         string parsedTown = location;
         int commaLoc = -1;
         do
         {
            commaLoc = parsedTown.IndexOf(',');
            if (commaLoc >= 0)
            {
               if (commaLoc > 0)
                  parsedAddress.Add(parsedTown.Substring(0, commaLoc));
               parsedTown = parsedTown.Substring(commaLoc + 1).TrimStart(' ');
            }
            else if (!string.IsNullOrEmpty(parsedTown))
               parsedAddress.Add(parsedTown);
         } while (commaLoc >= 0);

         int numLines = parsedAddress.Count;
         if (numLines > 0)
         {
            country = parsedAddress[numLines - 1];
            numLines--;
         }

         if (numLines > 0)
         {
            int spaceLoc = parsedAddress[numLines - 1].IndexOf(' ');
            if (spaceLoc > 0)
            {
               state = parsedAddress[numLines - 1].Substring(0, spaceLoc);
               postCode = parsedAddress[numLines - 1].Substring(spaceLoc + 1);
            }
            else
               state = parsedAddress[numLines - 1];
            numLines--;
         }

         if (numLines > 0)
         {
            city = parsedAddress[numLines - 1];
            numLines--;
         }

         List<string> addressLines = new List<string>();
         if (!string.IsNullOrEmpty(projectAddress))
            addressLines.Add(projectAddress);

         for (int ii = 0; ii < numLines; ii++)
         {
            addressLines.Add(parsedAddress[ii]);
         }

         postalAddress = IFCInstanceExporter.CreatePostalAddress(file, null, null, null,
            null, addressLines, null, city, state, postCode, country);

         return postalAddress;
      }

      private IFCAnyHandle CreateSIUnit(IFCFile file, ForgeTypeId specTypeId, IFCUnit ifcUnitType, IFCSIUnitName unitName, IFCSIPrefix? prefix, ForgeTypeId unitTypeId)
      {
         IFCAnyHandle siUnit = IFCInstanceExporter.CreateSIUnit(file, ifcUnitType, prefix, unitName);
         if (specTypeId != null && unitTypeId != null)
         {
            double scaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, unitTypeId);
            ExporterCacheManager.UnitsCache.AddUnit(specTypeId, siUnit, scaleFactor, 0.0);
         }

         return siUnit;
      }

      /// <summary>
      /// Создает IfcUnitAssignment. Это длинный список единиц, 
      /// которые мы правильно переводим из наших внутренних единиц в известные.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="doc">В документе представлены ProjectUnit и DisplayUnitSystem.</param>
      /// <returns>Дескриптор IFC.</returns>
      private IFCAnyHandle CreateDefaultUnits(ExporterIFC exporterIFC, Document doc)
      {
         HashSet<IFCAnyHandle> unitSet = new HashSet<IFCAnyHandle>();
         IFCFile file = exporterIFC.GetFile();
         bool exportToCOBIE = ExporterCacheManager.ExportOptionsCache.ExportAsCOBIE;

         Dictionary<Tuple<IFCAnyHandle, int>, IFCAnyHandle> addedDerivedUnitElements = new Dictionary<Tuple<IFCAnyHandle, int>, IFCAnyHandle>();
         Action<ISet<IFCAnyHandle>, IFCAnyHandle, int> createDerivedUnitElement = (elements, unit, exponent) =>
         {
            var pair = new Tuple<IFCAnyHandle, int>(unit, exponent);
            if (!addedDerivedUnitElements.ContainsKey(pair))
            {
               var element = IFCInstanceExporter.CreateDerivedUnitElement(file, unit, exponent);
               elements.Add(addedDerivedUnitElements[pair] = element);
            }

            elements.Add(addedDerivedUnitElements[pair]);
         };

         IFCAnyHandle lenSIBaseUnit = null;
         {
            bool lenConversionBased = false;
            bool lenUseDefault = false;

            IFCUnit lenUnitType = IFCUnit.LengthUnit;
            IFCSIPrefix? lenPrefix = null;
            IFCSIUnitName lenUnitName = IFCSIUnitName.Metre;
            string lenConvName = null;

            FormatOptions lenFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Length);
            ForgeTypeId lengthUnit = lenFormatOptions.GetUnitTypeId();
            if (lengthUnit.Equals(UnitTypeId.Meters) ||
               lengthUnit.Equals(UnitTypeId.MetersCentimeters))
            {
               // This space intentionally left blank
            }
            else if (lengthUnit.Equals(UnitTypeId.Centimeters))
            {
               lenPrefix = IFCSIPrefix.Centi;
            }
            else if (lengthUnit.Equals(UnitTypeId.Millimeters))
            {
               lenPrefix = IFCSIPrefix.Milli;
            }
            else if (lengthUnit.Equals(UnitTypeId.Feet) ||
               lengthUnit.Equals(UnitTypeId.FeetFractionalInches))
            {
               if (exportToCOBIE)
                  lenConvName = "foot";
               else
                  lenConvName = "FOOT";
               lenConversionBased = true;
            }
            else if (lengthUnit.Equals(UnitTypeId.FractionalInches) ||
               lengthUnit.Equals(UnitTypeId.Inches))
            {
               if (exportToCOBIE)
                  lenConvName = "inch";
               else
                  lenConvName = "INCH";
               lenConversionBased = true;
            }
            else
            {
               // Не удалось найти преобразование типа единицы отображения — предполагается, что фут
               if (exportToCOBIE)
                  lenConvName = "foot";
               else
                  lenConvName = "FOOT";
               lenConversionBased = true;
               lenUseDefault = true;
            }

            double lengthScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, lenUseDefault ? UnitTypeId.Feet : lenFormatOptions.GetUnitTypeId());
            IFCAnyHandle lenSIUnit = IFCInstanceExporter.CreateSIUnit(file, lenUnitType, lenPrefix, lenUnitName);
            if (lenPrefix == null)
               lenSIBaseUnit = lenSIUnit;
            else
               lenSIBaseUnit = IFCInstanceExporter.CreateSIUnit(file, lenUnitType, null, lenUnitName);

            if (lenConversionBased)
            {
               double lengthSIScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Meters) / lengthScaleFactor;
               IFCAnyHandle lenDims = IFCInstanceExporter.CreateDimensionalExponents(file, 1, 0, 0, 0, 0, 0, 0); // length
               IFCAnyHandle lenConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsLengthMeasure(lengthSIScaleFactor),
                   lenSIUnit);
               lenSIUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, lenDims, lenUnitType, lenConvName, lenConvFactor);
            }

            unitSet.Add(lenSIUnit);      // созданный выше, также уникальный.
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Length, lenSIUnit, lengthScaleFactor, 0.0);
         }

         {
            bool areaConversionBased = false;
            bool areaUseDefault = false;

            IFCUnit areaUnitType = IFCUnit.AreaUnit;
            IFCSIPrefix? areaPrefix = null;
            IFCSIUnitName areaUnitName = IFCSIUnitName.Square_Metre;
            string areaConvName = null;

            FormatOptions areaFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Area);
            ForgeTypeId areaUnit = areaFormatOptions.GetUnitTypeId();
            if (areaUnit.Equals(UnitTypeId.SquareMeters))
            {
               // Это место намеренно оставлено пустым.
            }
            else if (areaUnit.Equals(UnitTypeId.SquareCentimeters))
            {
               areaPrefix = IFCSIPrefix.Centi;
            }
            else if (areaUnit.Equals(UnitTypeId.SquareMillimeters))
            {
               areaPrefix = IFCSIPrefix.Milli;
            }
            else if (areaUnit.Equals(UnitTypeId.SquareFeet))
            {
               if (exportToCOBIE)
                  areaConvName = "foot";
               else
                  areaConvName = "SQUARE FOOT";
               areaConversionBased = true;
            }
            else if (areaUnit.Equals(UnitTypeId.SquareInches))
            {
               if (exportToCOBIE)
                  areaConvName = "inch";
               else
                  areaConvName = "SQUARE INCH";
               areaConversionBased = true;
            }
            else
            {
               // Не удалось найти преобразование типа единицы отображения — предполагается, что фут
               if (exportToCOBIE)
                  areaConvName = "foot";
               else
                  areaConvName = "SQUARE FOOT";
               areaConversionBased = true;
               areaUseDefault = true;
            }

            double areaScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, areaUseDefault ? UnitTypeId.SquareFeet : areaFormatOptions.GetUnitTypeId());
            IFCAnyHandle areaSiUnit = IFCInstanceExporter.CreateSIUnit(file, areaUnitType, areaPrefix, areaUnitName);
            if (areaConversionBased)
            {
               double areaSIScaleFactor = areaScaleFactor * UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.SquareMeters);
               IFCAnyHandle areaDims = IFCInstanceExporter.CreateDimensionalExponents(file, 2, 0, 0, 0, 0, 0, 0); // area
               IFCAnyHandle areaConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsAreaMeasure(areaSIScaleFactor), areaSiUnit);
               areaSiUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, areaDims, areaUnitType, areaConvName, areaConvFactor);
            }

            unitSet.Add(areaSiUnit);      // созданный выше, также уникальный.
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Area, areaSiUnit, areaScaleFactor, 0.0);
         }

         {
            bool volumeConversionBased = false;
            bool volumeUseDefault = false;

            IFCUnit volumeUnitType = IFCUnit.VolumeUnit;
            IFCSIPrefix? volumePrefix = null;
            IFCSIUnitName volumeUnitName = IFCSIUnitName.Cubic_Metre;
            string volumeConvName = null;

            FormatOptions volumeFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Volume);
            ForgeTypeId volumeUnit = volumeFormatOptions.GetUnitTypeId();
            if (volumeUnit.Equals(UnitTypeId.CubicMeters))
            {
               // Это место намеренно оставлено пустым.
            }
            else if (volumeUnit.Equals(UnitTypeId.Liters))
            {
               volumePrefix = IFCSIPrefix.Deci;
            }
            else if (volumeUnit.Equals(UnitTypeId.CubicCentimeters))
            {
               volumePrefix = IFCSIPrefix.Centi;
            }
            else if (volumeUnit.Equals(UnitTypeId.CubicMillimeters))
            {
               volumePrefix = IFCSIPrefix.Milli;
            }
            else if (volumeUnit.Equals(UnitTypeId.CubicFeet))
            {
               if (exportToCOBIE)
                  volumeConvName = "foot";
               else
                  volumeConvName = "CUBIC FOOT";
               volumeConversionBased = true;
            }
            else if (volumeUnit.Equals(UnitTypeId.CubicInches))
            {
               if (exportToCOBIE)
                  volumeConvName = "inch";
               else
                  volumeConvName = "CUBIC INCH";
               volumeConversionBased = true;
            }
            else
            {
               // Не удалось найти преобразование типа единицы отображения — предполагается, что фут
               if (exportToCOBIE)
                  volumeConvName = "foot";
               else
                  volumeConvName = "CUBIC FOOT";
               volumeConversionBased = true;
               volumeUseDefault = true;
            }

            double volumeScaleFactor =
                UnitUtils.ConvertFromInternalUnits(1.0, volumeUseDefault ? UnitTypeId.CubicFeet : volumeFormatOptions.GetUnitTypeId());
            IFCAnyHandle volumeSiUnit = IFCInstanceExporter.CreateSIUnit(file, volumeUnitType, volumePrefix, volumeUnitName);
            if (volumeConversionBased)
            {
               double volumeSIScaleFactor = volumeScaleFactor * UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.CubicMeters);
               IFCAnyHandle volumeDims = IFCInstanceExporter.CreateDimensionalExponents(file, 3, 0, 0, 0, 0, 0, 0); // volume
               IFCAnyHandle volumeConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsVolumeMeasure(volumeSIScaleFactor), volumeSiUnit);
               volumeSiUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, volumeDims, volumeUnitType, volumeConvName, volumeConvFactor);
            }

            unitSet.Add(volumeSiUnit);      // созданный выше, также уникальный.
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Volume, volumeSiUnit, volumeScaleFactor, 0.0);
         }

         IFCAnyHandle angleSIUnit = null;
         {
            IFCUnit unitType = IFCUnit.PlaneAngleUnit;
            IFCSIUnitName unitName = IFCSIUnitName.Radian;

            angleSIUnit = IFCInstanceExporter.CreateSIUnit(file, unitType, null, unitName);

            string convName = null;

            FormatOptions angleFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Angle);
            bool angleUseDefault = false;
            ForgeTypeId angleUnit = angleFormatOptions.GetUnitTypeId();
            if (angleUnit.Equals(UnitTypeId.Degrees) ||
               angleUnit.Equals(UnitTypeId.DegreesMinutes))
            {
               convName = "DEGREE";
            }
            else if (angleUnit.Equals(UnitTypeId.Gradians))
            {
               convName = "GRAD";
            }
            else if (angleUnit.Equals(UnitTypeId.Radians))
            {
               // Это место намеренно оставлено пустым.
            }
            else
            {
               angleUseDefault = true;
               convName = "DEGREE";
            }

            IFCAnyHandle dims = IFCInstanceExporter.CreateDimensionalExponents(file, 0, 0, 0, 0, 0, 0, 0);

            IFCAnyHandle planeAngleUnit = angleSIUnit;
            double angleScaleFactor = UnitUtils.Convert(1.0, angleUseDefault ? UnitTypeId.Degrees : angleFormatOptions.GetUnitTypeId(), UnitTypeId.Radians);
            if (convName != null)
            {
               IFCAnyHandle convFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsPlaneAngleMeasure(angleScaleFactor), planeAngleUnit);
               planeAngleUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, dims, unitType, convName, convFactor);
            }
            unitSet.Add(planeAngleUnit);      // созданный выше, также уникальный.
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Angle, planeAngleUnit, 1.0 / angleScaleFactor, 0.0);
         }

         // Mass
         IFCAnyHandle massSIUnit = null;
         {
            massSIUnit = CreateSIUnit(file, SpecTypeId.Mass, IFCUnit.MassUnit, IFCSIUnitName.Gram, IFCSIPrefix.Kilo, null);
            // Если мы экспортируем в стандарт GSA, мы заменим кг на фунты ниже.
            if (!exportToCOBIE)
               unitSet.Add(massSIUnit);      // созданный выше, такой уникальный.
         }

         // Массовая плотность — поддерживается только метрическая единица кг/(м^3).
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -3);

            IFCAnyHandle massDensityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.MassDensityUnit, null);
            unitSet.Add(massDensityUnit);

            double massDensityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerCubicMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.MassDensity, massDensityUnit, massDensityFactor, 0.0);
         }

         // Ion concentration - support metric kg/(m^3) only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -3);

            IFCAnyHandle massDensityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.IonConcentrationUnit, null);
            unitSet.Add(massDensityUnit);

            double massDensityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerCubicMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.PipingDensity, massDensityUnit, massDensityFactor, 0.0);
         }

         // Moment of inertia - support metric m^4.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 4);

            IFCAnyHandle momentOfInertiaUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.MomentOfInertiaUnit, null);
            unitSet.Add(momentOfInertiaUnit);

            double momentOfInertiaFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.MetersToTheFourthPower);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.MomentOfInertia, momentOfInertiaUnit, momentOfInertiaFactor, 0.0);
         }

         // Time -- support seconds only.
         IFCAnyHandle timeSIUnit = null;
         {
            timeSIUnit = CreateSIUnit(file, null, IFCUnit.TimeUnit, IFCSIUnitName.Second, null, null);
            unitSet.Add(timeSIUnit);      // created above, so unique.
         }

         // Frequency = support Hertz only.
         {
            IFCAnyHandle frequencySIUnit = CreateSIUnit(file, null, IFCUnit.FrequencyUnit, IFCSIUnitName.Hertz, null, null);
            unitSet.Add(frequencySIUnit);      // created above, so unique.
         }

         // Temperature
         IFCAnyHandle tempBaseSIUnit = null;
         {
            // Base SI unit for temperature.
            tempBaseSIUnit = CreateSIUnit(file, null, IFCUnit.ThermoDynamicTemperatureUnit, IFCSIUnitName.Kelvin, null, null);

            // We are going to have two entries: one for thermodynamic temperature (default), and one for color temperature.
            FormatOptions tempFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.HvacTemperature);
            IFCSIUnitName thermalTempUnit;
            double offset = 0.0;
            ForgeTypeId tempUnit = tempFormatOptions.GetUnitTypeId();
            if (tempUnit.Equals(UnitTypeId.Celsius) ||
               tempUnit.Equals(UnitTypeId.Fahrenheit))
            {
               thermalTempUnit = IFCSIUnitName.Degree_Celsius;
               offset = -273.15;
            }
            else
            {
               thermalTempUnit = IFCSIUnitName.Kelvin;
            }

            IFCAnyHandle temperatureSIUnit = null;
            if (thermalTempUnit != IFCSIUnitName.Kelvin)
               temperatureSIUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.ThermoDynamicTemperatureUnit, null, thermalTempUnit);
            else
               temperatureSIUnit = tempBaseSIUnit;
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacTemperature, temperatureSIUnit, 1.0, offset);

            unitSet.Add(temperatureSIUnit);      // created above, so unique.

            // Color temperature.
            // We don't add the color temperature to the unit set; it will be explicitly used.
            IFCAnyHandle colorTempSIUnit = tempBaseSIUnit;
            ExporterCacheManager.UnitsCache["COLORTEMPERATURE"] = colorTempSIUnit;
         }

         // Thermal transmittance - support metric W/(m^2 * K) = kg/(K * s^3) only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, tempBaseSIUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -3);

            IFCAnyHandle thermalTransmittanceUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.ThermalTransmittanceUnit, null);
            unitSet.Add(thermalTransmittanceUnit);

            double thermalTransmittanceFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.WattsPerSquareMeterKelvin);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HeatTransferCoefficient, thermalTransmittanceUnit, thermalTransmittanceFactor, 0.0);
         }

         // Thermal conductivity - support metric W/(m * K) = (kg * m)/(K * s^3) only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 1);
            createDerivedUnitElement(elements, tempBaseSIUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -3);

            IFCAnyHandle thermaConductivityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.ThermalConductanceUnit, null);
            unitSet.Add(thermaConductivityUnit);

            double thermalConductivityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.WattsPerMeterKelvin);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.ThermalConductivity, thermaConductivityUnit, thermalConductivityFactor, 0.0);
         }

         // Volumetric Flow Rate - support metric L/s or m^3/s only.
         {
            IFCAnyHandle volumetricFlowRateLenUnit = null;

            FormatOptions flowFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.AirFlow);
            ForgeTypeId forgeTypeId = flowFormatOptions.GetUnitTypeId();
            if (forgeTypeId.Equals(UnitTypeId.LitersPerSecond))
            {
               volumetricFlowRateLenUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.LengthUnit, IFCSIPrefix.Deci, IFCSIUnitName.Metre);
            }
            else
            {
               volumetricFlowRateLenUnit = lenSIBaseUnit;   // use m^3/s by default.
               forgeTypeId = UnitTypeId.CubicMetersPerSecond;
            }
            double volumetricFlowRateFactor = UnitUtils.ConvertFromInternalUnits(1.0, forgeTypeId);

            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, volumetricFlowRateLenUnit, 3);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle volumetricFlowRateUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.VolumetricFlowRateUnit, null);
            unitSet.Add(volumetricFlowRateUnit);

            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.AirFlow, volumetricFlowRateUnit, volumetricFlowRateFactor, 0.0);
         }

         // Mass flow rate - support kg/s only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle massFlowRateUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.MassFlowRateUnit, null);
            unitSet.Add(massFlowRateUnit);

            double massFlowRateFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.PipingMassPerTime, massFlowRateUnit, massFlowRateFactor, 0.0);
         }

         // Rotational frequency - support cycles/s only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle rotationalFrequencyUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.RotationalFrequencyUnit, null);
            unitSet.Add(rotationalFrequencyUnit);

            double rotationalFrequencyFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.RevolutionsPerSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.AngularSpeed, rotationalFrequencyUnit, rotationalFrequencyFactor, 0.0);
         }

         // Electrical current - support metric ampere only.
         IFCAnyHandle currentSIUnit = null;
         {
            currentSIUnit = CreateSIUnit(file, SpecTypeId.Current, IFCUnit.ElectricCurrentUnit, IFCSIUnitName.Ampere,
                null, UnitTypeId.Amperes);
            unitSet.Add(currentSIUnit);      // created above, so unique.
         }

         // Electrical voltage - support metric volt only.
         {
            IFCAnyHandle voltageSIUnit = CreateSIUnit(file, SpecTypeId.ElectricalPotential, IFCUnit.ElectricVoltageUnit, IFCSIUnitName.Volt,
                null, UnitTypeId.Volts);
            unitSet.Add(voltageSIUnit);      // created above, so unique.
         }

         // Power - support metric watt only.
         IFCAnyHandle powerSIUnit = null;
         {
            powerSIUnit = CreateSIUnit(file, SpecTypeId.HvacPower, IFCUnit.PowerUnit, IFCSIUnitName.Watt,
                null, UnitTypeId.Watts);
            unitSet.Add(powerSIUnit);      // created above, so unique.
         }

         // Force - support newtons (N), metric weight-force, us-weight-force and kips.
         {
            bool forceConversionBased = false;
            bool forceUseDefault = false;

            IFCUnit forceUnitType = IFCUnit.ForceUnit;
            IFCSIPrefix? forcePrefix = null;
            IFCSIUnitName forceUnitName = IFCSIUnitName.Newton;
            string forceConvName = null;

            FormatOptions forceFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Force);
            ForgeTypeId forceUnit = forceFormatOptions.GetUnitTypeId();
            if (forceUnit.Equals(UnitTypeId.Newtons))
            {
               // This space intentionally left blank.
            }
            else if (forceUnit.Equals(UnitTypeId.Dekanewtons))
            {
               forcePrefix = IFCSIPrefix.Deca;
            }
            else if (forceUnit.Equals(UnitTypeId.Kilonewtons))
            {
               forcePrefix = IFCSIPrefix.Kilo;
            }
            else if (forceUnit.Equals(UnitTypeId.Meganewtons))
            {
               forcePrefix = IFCSIPrefix.Mega;
            }
            else if (forceUnit.Equals(UnitTypeId.KilogramsForce))
            {
               forceConvName = "KILOGRAM-FORCE";
            }
            else if (forceUnit.Equals(UnitTypeId.TonnesForce))
            {
               forceConvName = "TONN-FORCE";
            }
            else if (forceUnit.Equals(UnitTypeId.UsTonnesForce))
            {
               forceConvName = "USTONN-FORCE";
               forceConversionBased = true;
            }
            else if (forceUnit.Equals(UnitTypeId.PoundsForce))
            {
               forceConvName = "POUND-FORCE";
               forceConversionBased = true;
            }
            else if (forceUnit.Equals(UnitTypeId.Kips))
            {
               forceConvName = "KIP";
               forceConversionBased = true;
            }
            else
            {
               //Couldn't find display unit type conversion -- assuming newton
               forceUnit = UnitTypeId.Newtons;
               forceUseDefault = true;
            }

            if (exportToCOBIE && forceConvName != null)
               forceConvName = forceConvName.ToLower();

            double forceScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, forceUseDefault ? UnitTypeId.Newtons : forceFormatOptions.GetUnitTypeId());
            IFCAnyHandle forceSiUnit = IFCInstanceExporter.CreateSIUnit(file, forceUnitType, forcePrefix, forceUnitName);
            if (forceConversionBased)
            {
               double forceSIScaleFactor = forceScaleFactor * UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Newtons);
               IFCAnyHandle forceDims = IFCInstanceExporter.CreateDimensionalExponents(file, 1, 1, -2, 0, 0, 0, 0); // force
               IFCAnyHandle forceConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsForceMeasure(forceSIScaleFactor), forceSiUnit);
               forceSiUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, forceDims, forceUnitType, forceConvName, forceConvFactor);
            }

            unitSet.Add(forceSiUnit);      // created above, so unique.
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Force, forceSiUnit, forceScaleFactor, 0.0);
         }

         // Illuminance
         {
            IFCSIPrefix? prefix = null;
            IFCAnyHandle luxSIUnit = CreateSIUnit(file, SpecTypeId.Illuminance, IFCUnit.IlluminanceUnit, IFCSIUnitName.Lux,
                prefix, UnitTypeId.Lux);
            unitSet.Add(luxSIUnit);      // created above, so unique.
            ExporterCacheManager.UnitsCache["LUX"] = luxSIUnit;
         }

         // Luminous Flux
         IFCAnyHandle lumenSIUnit = null;
         {
            IFCSIPrefix? prefix = null;
            lumenSIUnit = CreateSIUnit(file, SpecTypeId.LuminousFlux, IFCUnit.LuminousFluxUnit, IFCSIUnitName.Lumen,
                prefix, UnitTypeId.Lumens);
            unitSet.Add(lumenSIUnit);      // created above, so unique.
         }

         // Luminous Intensity
         {
            IFCSIPrefix? prefix = null;
            IFCAnyHandle candelaSIUnit = CreateSIUnit(file, SpecTypeId.LuminousIntensity, IFCUnit.LuminousIntensityUnit, IFCSIUnitName.Candela,
                prefix, UnitTypeId.Candelas);
            unitSet.Add(candelaSIUnit);      // created above, so unique.
         }

         // Energy
         {
            IFCSIPrefix? prefix = null;
            IFCAnyHandle jouleSIUnit = CreateSIUnit(file, SpecTypeId.Energy, IFCUnit.EnergyUnit, IFCSIUnitName.Joule,
                prefix, UnitTypeId.Joules);
            unitSet.Add(jouleSIUnit);      // created above, so unique.
         }

         // Luminous Efficacy - support lm/W only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, -1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -2);
            createDerivedUnitElement(elements, timeSIUnit, 3);
            createDerivedUnitElement(elements, lumenSIUnit, 1);

            IFCAnyHandle luminousEfficacyUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.UserDefined, "Luminous Efficacy");

            double electricalEfficacyFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.LumensPerWatt);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Efficacy, luminousEfficacyUnit, electricalEfficacyFactor, 0.0);
            ExporterCacheManager.UnitsCache["LUMINOUSEFFICACY"] = luminousEfficacyUnit;

            unitSet.Add(luminousEfficacyUnit);
         }

         // Electrical Resistivity - support Ohm * M = (kg * m^3)/(s^3 * A^2) only
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 3);
            createDerivedUnitElement(elements, timeSIUnit, -3);
            createDerivedUnitElement(elements, currentSIUnit, -2);

            IFCAnyHandle electricalResistivityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.UserDefined, "Electrical Resistivity");

            double electricalResistivityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.OhmMeters);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.ElectricalResistivity, electricalResistivityUnit, electricalResistivityFactor, 0.0);
            ExporterCacheManager.UnitsCache["ELECTRICALRESISTIVITY"] = electricalResistivityUnit;

            unitSet.Add(electricalResistivityUnit);
         }
         // Sound Power - support watt only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
            createDerivedUnitElement(elements, timeSIUnit, -3);

            IFCAnyHandle soundPowerUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.SoundPowerUnit, null);
            unitSet.Add(soundPowerUnit);

            double soundPowerFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Watts);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Wattage, soundPowerUnit, soundPowerFactor, 0.0);
         }

         // Sound Pressure - support Pascal only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle soundPressureUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.SoundPressureUnit, null);
            unitSet.Add(soundPressureUnit);

            double soundPressureFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Pascals);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacPressure, soundPressureUnit, soundPressureFactor, 0.0);
         }

         // Linear Velocity - support m/s only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle linearVelocityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.LinearVelocityUnit, null);

            double linearVelocityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.MetersPerSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacVelocity, linearVelocityUnit, linearVelocityFactor, 0.0);

            unitSet.Add(linearVelocityUnit);
         }

         // Currency - disallowed for IC2x3 Coordination View 2.0.  If we find a currency, export it as a real.
         if (!ExporterCacheManager.ExportOptionsCache.ExportAs2x3CoordinationView2)
         {
            FormatOptions currencyFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.Currency);
            ForgeTypeId currencySymbol = currencyFormatOptions.GetSymbolTypeId();

            IFCAnyHandle currencyUnit = null;

            // Some of these are guesses for IFC2x3, since multiple currencies may use the same symbol, 
            // but no detail is given on which currency is being used.  For IFC4, we just use the label.
            if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               string currencyLabel = null;
               try
               {
                  currencyLabel = LabelUtils.GetLabelForSymbol(currencySymbol);
                  currencyUnit = IFCInstanceExporter.CreateMonetaryUnit4(file, currencyLabel);
               }
               catch
               {
                  currencyUnit = null;
               }
            }
            else
            {
               IFCCurrencyType? currencyType = null;

               if (currencySymbol.Equals(SymbolTypeId.UsDollar))
               {
                  currencyType = IFCCurrencyType.USD;
               }
               else if (currencySymbol.Equals(SymbolTypeId.EuroPrefix) ||
                  currencySymbol.Equals(SymbolTypeId.EuroSuffix))
               {
                  currencyType = IFCCurrencyType.EUR;
               }
               else if (currencySymbol.Equals(SymbolTypeId.UkPound))
               {
                  currencyType = IFCCurrencyType.GBP;
               }
               else if (currencySymbol.Equals(SymbolTypeId.ChineseHongKongDollar))
               {
                  currencyType = IFCCurrencyType.HKD;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Krone))
               {
                  currencyType = IFCCurrencyType.NOK;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Shekel))
               {
                  currencyType = IFCCurrencyType.ILS;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Yen))
               {
                  currencyType = IFCCurrencyType.JPY;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Won))
               {
                  currencyType = IFCCurrencyType.KRW;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Baht))
               {
                  currencyType = IFCCurrencyType.THB;
               }
               else if (currencySymbol.Equals(SymbolTypeId.Dong))
               {
                  currencyType = IFCCurrencyType.VND;
               }

               if (currencyType.HasValue)
                  currencyUnit = IFCInstanceExporter.CreateMonetaryUnit2x3(file, currencyType.Value);
            }

            if (currencyUnit != null)
            {
               unitSet.Add(currencyUnit);      // created above, so unique.
               // We will cache the currency, if we create it.  If we don't, we'll export currencies as numbers.
               ExporterCacheManager.UnitsCache["CURRENCY"] = currencyUnit;
            }
         }

         // Pressure - support Pascal, kPa and MPa.
         {
            IFCSIPrefix? prefix = null;
            FormatOptions pressureFormatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.HvacPressure);
            ForgeTypeId pressureUnit = pressureFormatOptions.GetUnitTypeId();
            if (pressureUnit.Equals(UnitTypeId.Pascals))
            {
               // This space intentionally left blank.
            }
            else if (pressureUnit.Equals(UnitTypeId.Kilopascals))
            {
               prefix = IFCSIPrefix.Kilo;
            }
            else if (pressureUnit.Equals(UnitTypeId.Megapascals))
            {
               prefix = IFCSIPrefix.Mega;
            }
            else
            {
               pressureUnit = UnitTypeId.Pascals;
            }

            IFCAnyHandle pressureSIUnit = CreateSIUnit(file, SpecTypeId.HvacPressure, IFCUnit.PressureUnit, IFCSIUnitName.Pascal,
                prefix, pressureUnit);
            unitSet.Add(pressureSIUnit);      // created above, so unique.
         }

         // Friction loss - support Pa/m only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, -2);
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle frictionLossUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.UserDefined, "Friction Loss");

            double frictionLossFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.PascalsPerMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacFriction, frictionLossUnit, frictionLossFactor, 0.0);
            ExporterCacheManager.UnitsCache["FRICTIONLOSS"] = frictionLossUnit;

            unitSet.Add(frictionLossUnit);
         }

         // Area/Planar Force - support N/m2 only, and Linear Force - support N/m only
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle linearForceUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.LinearForceUnit, null);

            double linearForceFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NewtonsPerMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.LinearForce, linearForceUnit, linearForceFactor, 0.0);
            unitSet.Add(linearForceUnit);

            elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle planarForceUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.PlanarForceUnit, null);

            double planarForceFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NewtonsPerSquareMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.AreaForce, planarForceUnit, planarForceFactor, 0.0);
            unitSet.Add(planarForceUnit);
         }

         // Specific heat - support metric J/(kg * K) = m^2/(s^2 * K) only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
            createDerivedUnitElement(elements, timeSIUnit, -2);
            createDerivedUnitElement(elements, tempBaseSIUnit, -1);

            IFCAnyHandle specificHeatUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.SpecificHeatCapacityUnit, null);
            unitSet.Add(specificHeatUnit);

            double specificHeatFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.JoulesPerKilogramDegreeCelsius);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.SpecificHeat, specificHeatUnit, specificHeatFactor, 0.0);
         }

         // Heat flux density - support metric W/m^2 = kg/s^3 only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -3);

            IFCAnyHandle heatFluxDensityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.HeatFluxDensityUnit, null);
            unitSet.Add(heatFluxDensityUnit);

            double heatFluxDensityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.WattsPerSquareMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacPowerDensity, heatFluxDensityUnit, heatFluxDensityFactor, 0.0);
         }

         // Heating value - support metric J/kg = m^2/s^2 only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle heatingValueUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.HeatingValueUnit, null);
            unitSet.Add(heatingValueUnit);

            double heatingValueFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.JoulesPerGram);
            heatingValueFactor *= 1.0e+3; // --> gram to kilogram
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.SpecificHeatOfVaporization, heatingValueUnit, heatingValueFactor, 0.0);
         }

         // Permeability (Permeance) - support metric kg/(Pa * s * m^2) = s/m only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, timeSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);

            IFCAnyHandle permeabilityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.VaporPermeabilityUnit, null);
            unitSet.Add(permeabilityUnit);

            double permeabilityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NanogramsPerPascalSecondSquareMeter);
            permeabilityFactor *= 1.0e-12; // --> Nanogram to kilogram
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Permeability, permeabilityUnit, permeabilityFactor, 0.0);
         }

         // Dynamic viscosity - support metric Pa * s = kg/(m * s) only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle viscosityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.DynamicViscosityUnit, null);
            unitSet.Add(viscosityUnit);

            double viscosityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerMeterSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.HvacViscosity, viscosityUnit, viscosityFactor, 0.0);
         }

         // Thermal expansion coefficient - support metric 1/K only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, tempBaseSIUnit, -1);

            IFCAnyHandle thermalExpansionCoefficientUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.ThermalExpansionCoefficientUnit, null);
            unitSet.Add(thermalExpansionCoefficientUnit);

            double thermalExpansionCoefficientFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.InverseDegreesCelsius);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.ThermalExpansionCoefficient, thermalExpansionCoefficientUnit, thermalExpansionCoefficientFactor, 0.0);
         }

         // Modulus of elasticity - support Pascal only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle modulusOfElasticityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.ModulusOfElasticityUnit, null);
            unitSet.Add(modulusOfElasticityUnit);

            double modulusOfElasticityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Pascals);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Stress, modulusOfElasticityUnit, modulusOfElasticityFactor, 0.0);
         }

         // Isothermal moisture capacity - support m3 / kg only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 3);
            createDerivedUnitElement(elements, massSIUnit, -1);

            IFCAnyHandle isothermalMoistureCapacityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.IsothermalMoistureCapacityUnit, null);
            unitSet.Add(isothermalMoistureCapacityUnit);

            double isothermalMoistureCapacityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.CubicMetersPerKilogram);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.IsothermalMoistureCapacity, isothermalMoistureCapacityUnit, isothermalMoistureCapacityFactor, 0.0);
         }

         // Moisture diffusivity - support metric m^2/s only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle moistureDiffusivityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.MoistureDiffusivityUnit, null);
            unitSet.Add(moistureDiffusivityUnit);

            double moistureDiffusivityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.SquareMetersPerSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Diffusivity, moistureDiffusivityUnit, moistureDiffusivityFactor, 0.0);
         }

         // Area density - support metric kg/m^2 only.
         if (!ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
         
            IFCAnyHandle areaDensityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                Toolkit.IFC4.IFCDerivedUnit.AREADENSITYUNIT, null);
            unitSet.Add(areaDensityUnit);
         
            double areaDensityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerSquareMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.MassPerUnitArea, areaDensityUnit, areaDensityFactor, 0.0);
         }

         // Mass per length - support metric kg/m only.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, -1);

            IFCAnyHandle massPerLenghtUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.MassPerLengthUnit, null);
            unitSet.Add(massPerLenghtUnit);

            double massPerLenghtFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.KilogramsPerMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.MassPerUnitLength, massPerLenghtUnit, massPerLenghtFactor, 0.0);
         }

         // Thermal resistance - support metric (m^2 * K)/W = (s^3 * K) / kg only. 
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, timeSIUnit, 3);
            createDerivedUnitElement(elements, tempBaseSIUnit, 1);
            createDerivedUnitElement(elements, massSIUnit, -1);

            IFCAnyHandle thermalResistanceUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.ThermalResistanceUnit, null);
            unitSet.Add(thermalResistanceUnit);

            double thermalResistanceFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.SquareMeterKelvinsPerWatt);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.ThermalResistance, thermalResistanceUnit, thermalResistanceFactor, 0.0);
         }

         // Acceleration - support metric m/s^2 only. 
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            createDerivedUnitElement(elements, lenSIBaseUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle thermalResistanceUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.AccelerationUnit, null);
            unitSet.Add(thermalResistanceUnit);

            double thermalResistanceFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.MetersPerSecondSquared);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Acceleration, thermalResistanceUnit, thermalResistanceFactor, 0.0);
         }

         // Угловая скорость — поддерживается только rad/s.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();
            
            createDerivedUnitElement(elements, angleSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -1);

            IFCAnyHandle angularVelocityUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.AngularVelocityUnit, null);
            unitSet.Add(angularVelocityUnit);

            double angularVelocityFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.RadiansPerSecond);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Pulsation, angularVelocityUnit, angularVelocityFactor, 0.0);
         }

         // Линейная жесткость - только N/m = kg/s^2.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();

            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle linearStiffnessUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.LinearStiffnessUnit, null);
            unitSet.Add(linearStiffnessUnit);

            double linearStiffnessFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NewtonsPerMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.PointSpringCoefficient, linearStiffnessUnit, linearStiffnessFactor, 0.0);
         }

         // Константа деформации — поддерживается только m^6.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();

            createDerivedUnitElement(elements, lenSIBaseUnit, 6);

            IFCAnyHandle wrappingConstantUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.WarpingConstantUnit, null);
            unitSet.Add(wrappingConstantUnit);

            double wrappingConstantFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.MetersToTheSixthPower);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.WarpingConstant, wrappingConstantUnit, wrappingConstantFactor, 0.0);
         }

         // Линейный момент - поддерживается только N-m / m = (kg * m)/s^2.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();

            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 1);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle linearMomentUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.LinearMomentUnit, null);
            unitSet.Add(linearMomentUnit);

            double linearMomentFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NewtonMetersPerMeter);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.LinearMoment, linearMomentUnit, linearMomentFactor, 0.0);
         }

         // Крутящий момент — поддержка только N-m = (kg * m^2)/s^2.
         {
            ISet<IFCAnyHandle> elements = new HashSet<IFCAnyHandle>();

            createDerivedUnitElement(elements, massSIUnit, 1);
            createDerivedUnitElement(elements, lenSIBaseUnit, 2);
            createDerivedUnitElement(elements, timeSIUnit, -2);

            IFCAnyHandle torqueUnit = IFCInstanceExporter.CreateDerivedUnit(file, elements,
                IFCDerivedUnitEnum.Torqueunit, null);
            unitSet.Add(torqueUnit);

            double torqueFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.NewtonMeters);
            ExporterCacheManager.UnitsCache.AddUnit(SpecTypeId.Moment, torqueUnit, torqueFactor, 0.0);
         }

         // GSA только единицы.
         if (exportToCOBIE)
         {
            // Производная имперская единица массы
            {
               IFCUnit unitType = IFCUnit.MassUnit;
               IFCAnyHandle dims = IFCInstanceExporter.CreateDimensionalExponents(file, 0, 1, 0, 0, 0, 0, 0);
               double factor = 0.45359237; // --> фунт в килограмм
               string convName = "pound";

               IFCAnyHandle convFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsMassMeasure(factor), massSIUnit);
               IFCAnyHandle massUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, dims, unitType, convName, convFactor);
               unitSet.Add(massUnit);      // созданный выше, также уникальный.
            }

            // Воздухообмен в час
            {
               IFCUnit unitType = IFCUnit.FrequencyUnit;
               IFCAnyHandle dims = IFCInstanceExporter.CreateDimensionalExponents(file, 0, 0, -1, 0, 0, 0, 0);
               double factor = 1.0 / 3600.0; // --> секунды в часы
               string convName = "ACH";

               IFCAnyHandle convFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsTimeMeasure(factor), timeSIUnit);
               IFCAnyHandle achUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, dims, unitType, convName, convFactor);
               unitSet.Add(achUnit);      // созданный выше, также уникальный.
               ExporterCacheManager.UnitsCache["ACH"] = achUnit;
            }
         }

         return IFCInstanceExporter.CreateUnitAssignment(file, unitSet);
      }

      /// <summary>
      /// Создает глобальное направление и устанавливает основные направления в 3D.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      private void CreateGlobalDirection(ExporterIFC exporterIFC)
      {
         // Обратите внимание, что мы не используем приведенные ниже функции ExporterUtil.CreateDirection,
         // поскольку они пытаются чтобы сопоставить входной сигнал XYZ
         // с одним из «глобальных» направлений, которые мы создаем ниже.
         IFCAnyHandle xDirPos = null;
         IFCAnyHandle xDirNeg = null;
         IFCAnyHandle yDirPos = null;
         IFCAnyHandle yDirNeg = null;
         IFCAnyHandle zDirPos = null;
         IFCAnyHandle zDirNeg = null;

         IFCFile file = exporterIFC.GetFile();
         IList<double> xxp = new List<double>();
         xxp.Add(1.0); xxp.Add(0.0); xxp.Add(0.0);
         xDirPos = IFCInstanceExporter.CreateDirection(file, xxp);

         IList<double> xxn = new List<double>();
         xxn.Add(-1.0); xxn.Add(0.0); xxn.Add(0.0);
         xDirNeg = IFCInstanceExporter.CreateDirection(file, xxn);

         IList<double> yyp = new List<double>();
         yyp.Add(0.0); yyp.Add(1.0); yyp.Add(0.0);
         yDirPos = IFCInstanceExporter.CreateDirection(file, yyp);

         IList<double> yyn = new List<double>();
         yyn.Add(0.0); yyn.Add(-1.0); yyn.Add(0.0);
         yDirNeg = IFCInstanceExporter.CreateDirection(file, yyn);

         IList<double> zzp = new List<double>();
         zzp.Add(0.0); zzp.Add(0.0); zzp.Add(1.0);
         zDirPos = IFCInstanceExporter.CreateDirection(file, zzp);

         IList<double> zzn = new List<double>();
         zzn.Add(0.0); zzn.Add(0.0); zzn.Add(-1.0);
         zDirNeg = IFCInstanceExporter.CreateDirection(file, zzn);

         ExporterIFCUtils.SetGlobal3DDirectionHandles(true, xDirPos, yDirPos, zDirPos);
         ExporterIFCUtils.SetGlobal3DDirectionHandles(false, xDirNeg, yDirNeg, zDirNeg);
      }

      /// <summary>
      /// Создает глобальное направление и устанавливает основные направления в 2D.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      private void CreateGlobalDirection2D(ExporterIFC exporterIFC)
      {
         IFCAnyHandle xDirPos2D = null;
         IFCAnyHandle xDirNeg2D = null;
         IFCAnyHandle yDirPos2D = null;
         IFCAnyHandle yDirNeg2D = null;
         IFCFile file = exporterIFC.GetFile();

         IList<double> xxp = new List<double>();
         xxp.Add(1.0); xxp.Add(0.0);
         xDirPos2D = IFCInstanceExporter.CreateDirection(file, xxp);

         IList<double> xxn = new List<double>();
         xxn.Add(-1.0); xxn.Add(0.0);
         xDirNeg2D = IFCInstanceExporter.CreateDirection(file, xxn);

         IList<double> yyp = new List<double>();
         yyp.Add(0.0); yyp.Add(1.0);
         yDirPos2D = IFCInstanceExporter.CreateDirection(file, yyp);

         IList<double> yyn = new List<double>();
         yyn.Add(0.0); yyn.Add(-1.0);
         yDirNeg2D = IFCInstanceExporter.CreateDirection(file, yyn);
         ExporterIFCUtils.SetGlobal2DDirectionHandles(true, xDirPos2D, yDirPos2D);
         ExporterIFCUtils.SetGlobal2DDirectionHandles(false, xDirNeg2D, yDirNeg2D);
      }

      /// <summary>
      /// Создает глобальное декартово начало координат, а затем устанавливает начало координат 3D и 2D.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      private void CreateGlobalCartesianOrigin(ExporterIFC exporterIFC)
      {

         IFCAnyHandle origin2d = null;
         IFCAnyHandle origin = null;

         IFCFile file = exporterIFC.GetFile();
         IList<double> measure = new List<double>();
         measure.Add(0.0); measure.Add(0.0); measure.Add(0.0);
         origin = IFCInstanceExporter.CreateCartesianPoint(file, measure);

         IList<double> measure2d = new List<double>();
         measure2d.Add(0.0); measure2d.Add(0.0);
         origin2d = IFCInstanceExporter.CreateCartesianPoint(file, measure2d);
         ExporterIFCUtils.SetGlobal3DOriginHandle(origin);
         ExporterIFCUtils.SetGlobal2DOriginHandle(origin2d);
      }

      private static bool ValidateContainedHandle(IFCAnyHandle initialHandle)
      {
         if (ExporterCacheManager.ElementsInAssembliesCache.Contains(initialHandle))
            return false;

         try
         {
            if (!IFCAnyHandleUtil.HasRelDecomposes(initialHandle))
               return true;
         }
         catch
         {
         }

         return false;
      }

      /// <summary>
      /// Удалите содержащиеся или недопустимые дескрипторы из этого набора.
      /// </summary>
      /// <param name="initialSet">Исходный набор, который может содержать или содержать недопустимые дескрипторы.</param>
      /// <returns>Очищенный набор.</returns>
      public static HashSet<IFCAnyHandle> RemoveContainedHandlesFromSet(ICollection<IFCAnyHandle> initialSet)
      {
         HashSet<IFCAnyHandle> filteredSet = new HashSet<IFCAnyHandle>();

         if (initialSet != null)
         {
            foreach (IFCAnyHandle initialHandle in initialSet)
            {
               if (ValidateContainedHandle(initialHandle))
                  filteredSet.Add(initialHandle);
            }
         }

         return filteredSet;
      }

      private class IFCLevelExportInfo
      {
         public IFCLevelExportInfo() { }

         public IDictionary<ElementId, IList<IFCLevelInfo>> LevelMapping { get; set; } =
            new Dictionary<ElementId, IList<IFCLevelInfo>>();

         public IList<IFCLevelInfo> OrphanedLevelInfos { get; set; } = new List<IFCLevelInfo>();

         public void UnionLevelInfoRelated(ElementId toLevelId, IFCLevelInfo fromLevel)
         {
            if (fromLevel == null)
               return;

            if (toLevelId == ElementId.InvalidElementId)
            {
               OrphanedLevelInfos.Add(fromLevel);
               return;
            }

            IList<IFCLevelInfo> levelMappingList;
            if (!LevelMapping.TryGetValue(toLevelId, out levelMappingList))
            {
               levelMappingList = new List<IFCLevelInfo>();
               LevelMapping[toLevelId] = levelMappingList;
            }
            levelMappingList.Add(fromLevel);
         }

         public void TransferOrphanedLevelInfo(ElementId toLevelId)
         {
            if (toLevelId == ElementId.InvalidElementId)
               return;

            if (OrphanedLevelInfos.Count == 0)
               return;

            IList<IFCLevelInfo> toLevelMappingList;
            if (!LevelMapping.TryGetValue(toLevelId, out toLevelMappingList))
            {
               toLevelMappingList = new List<IFCLevelInfo>();
               LevelMapping[toLevelId] = toLevelMappingList;
            }

            foreach (IFCLevelInfo orphanedLevelInfo in OrphanedLevelInfos)
            {
               toLevelMappingList.Add(orphanedLevelInfo);
            }
            OrphanedLevelInfos.Clear();
         }

         public Tuple<HashSet<IFCAnyHandle>, HashSet<IFCAnyHandle>> CollectValidHandlesForLevel(
            ElementId levelId, IFCLevelInfo levelInfo)
         {
            if (levelId == ElementId.InvalidElementId || levelInfo == null)
               return null;

            HashSet<IFCAnyHandle> levelRelatedProducts = new HashSet<IFCAnyHandle>();
            levelRelatedProducts.UnionWith(levelInfo.GetRelatedProducts());

            HashSet<IFCAnyHandle> levelRelatedElements = new HashSet<IFCAnyHandle>();
            levelRelatedElements.UnionWith(levelInfo.GetRelatedElements());

            IList<IFCLevelInfo> levelMappingList;
            if (LevelMapping.TryGetValue(levelId, out levelMappingList))
            {
               foreach (IFCLevelInfo containedLevelInfo in levelMappingList)
               {
                  if (containedLevelInfo != null)
                  {
                     levelRelatedProducts.UnionWith(containedLevelInfo.GetRelatedProducts());
                     levelRelatedElements.UnionWith(containedLevelInfo.GetRelatedElements());
                  }
               }
            }

            return Tuple.Create(RemoveContainedHandlesFromSet(levelRelatedProducts),
               RemoveContainedHandlesFromSet(levelRelatedElements));
         }

         public HashSet<IFCAnyHandle> CollectOrphanedHandles()
         {
            HashSet<IFCAnyHandle> orphanedHandles = new HashSet<IFCAnyHandle>();
            foreach (IFCLevelInfo containedLevelInfo in OrphanedLevelInfos)
            {
               if (containedLevelInfo != null)
               {
                  orphanedHandles.UnionWith(containedLevelInfo.GetRelatedProducts());
                  orphanedHandles.UnionWith(containedLevelInfo.GetRelatedElements());
               }
            }

            // RemoveContainedHandlesFromSet будет вызываться до их использования,
            // поэтому не утруждайтесь делать это дважды.
            return orphanedHandles;
         }
      }

      private IFCExportElement GetExportLevelState(Element level)
      {
         if (level == null)
            return IFCExportElement.ByType;

         Element levelType = level.Document.GetElement(level.GetTypeId());
         IFCExportElement? exportLevel =
            ElementFilteringUtil.GetExportElementState(level, levelType);

         return exportLevel.GetValueOrDefault(IFCExportElement.ByType);
      }
      
      private bool NeverExportLevel(Element level)
      {
         return GetExportLevelState(level) == IFCExportElement.No;
      }

      private bool AlwaysExportLevel(Element level)
      {
         return GetExportLevelState(level) == IFCExportElement.Yes;
      }

      /// <summary>
      /// Свяpь уровней и продуктов.
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="document">Документ для связи уровней.</param>
      private void RelateLevels(ExporterIFC exporterIFC, Document document)
      {
         HashSet<IFCAnyHandle> buildingStories = new HashSet<IFCAnyHandle>();
         IList<ElementId> levelIds = ExporterCacheManager.LevelInfoCache.LevelsByElevation;
         IFCFile file = exporterIFC.GetFile();

         ElementId lastValidLevelId = ElementId.InvalidElementId;
         IFCLevelExportInfo levelInfoMapping = new IFCLevelExportInfo();

         for (int ii = 0; ii < levelIds.Count; ii++)
         {
            ElementId levelId = levelIds[ii];
            IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(exporterIFC, levelId);
            if (levelInfo == null)
               continue;

            Element level = document.GetElement(levelId);

            levelInfoMapping.TransferOrphanedLevelInfo(levelId);
            int nextLevelIdx = ii + 1;

            // Здесь могут быть устаревшие дескрипторы; защититься от этого.
            Tuple<HashSet<IFCAnyHandle>, HashSet<IFCAnyHandle>> productsAndElements =
               levelInfoMapping.CollectValidHandlesForLevel(levelId, levelInfo);

            HashSet<IFCAnyHandle> relatedProducts = productsAndElements.Item1;
            HashSet<IFCAnyHandle> relatedElements = productsAndElements.Item2;

            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, false))
            {
               IFCAnyHandle buildingStoreyHandle = levelInfo.GetBuildingStorey();
               if (!buildingStories.Contains(buildingStoreyHandle))
               {
                  buildingStories.Add(buildingStoreyHandle);
                  IFCExportInfoPair exportInfo = new IFCExportInfoPair(IFCEntityType.IfcBuildingStorey);

                  // Добавить набор объектов недвижимости, количество и классификацию этажей здания также в IFC.
                  productWrapper.AddElement(level, buildingStoreyHandle, levelInfo, null, false, exportInfo);

                  ExporterUtil.ExportRelatedProperties(exporterIFC, level, productWrapper);
               }
            }

            if (relatedProducts.Count > 0)
            {
               IFCAnyHandle buildingStorey = levelInfo.GetBuildingStorey();
               string guid = GUIDUtil.CreateSubElementGUID(level, (int)IFCBuildingStoreySubElements.RelAggregates);
               ExporterCacheManager.ContainmentCache.AddRelations(buildingStorey, guid, relatedProducts);
            }

            if (relatedElements.Count > 0)
            {
               string guid = GUIDUtil.CreateSubElementGUID(level, (int)IFCBuildingStoreySubElements.RelContainedInSpatialStructure);
               IFCInstanceExporter.CreateRelContainedInSpatialStructure(file, guid, ExporterCacheManager.OwnerHistoryHandle, null, null, relatedElements, levelInfo.GetBuildingStorey());
            }

            ii = nextLevelIdx - 1;
         }

         if (buildingStories.Count > 0)
         {
            IFCAnyHandle buildingHnd = ExporterCacheManager.BuildingHandle;
            ProjectInfo projectInfo = document.ProjectInformation;
            string guid = GUIDUtil.CreateSubElementGUID(projectInfo, (int)IFCProjectSubElements.RelAggregatesBuildingStories);
            ExporterCacheManager.ContainmentCache.AddRelations(buildingHnd, guid, buildingStories);
         }

         // Мы не нашли для этого уровня. Поместите его в IfcBuilding, IfcSite или IfcProject позже.
         HashSet<IFCAnyHandle> orphanedHandles = levelInfoMapping.CollectOrphanedHandles();

         ExporterCacheManager.LevelInfoCache.OrphanedElements.UnionWith(orphanedHandles);
      }

      /// <summary>
      /// Очистить всех делегатов.
      /// </summary>
      private void DelegateClear()
      {
         m_ElementExporter = null;
         m_QuantitiesToExport = null;
      }

      private IFCAnyHandle CreateBuildingPlacement(IFCFile file)
      {
         return IFCInstanceExporter.CreateLocalPlacement(file, null, ExporterUtil.CreateAxis2Placement3D(file));
      }

      private IFCAnyHandle CreateBuildingFromProjectInfo(ExporterIFC exporterIFC, Document document, IFCAnyHandle buildingPlacement)
      {
         ProjectInfo projectInfo = document.ProjectInformation;
         IFCAnyHandle ownerHistory = ExporterCacheManager.OwnerHistoryHandle;

         string buildingName = string.Empty;
         string buildingDescription = null;
         string buildingLongName = null;
         string buildingObjectType = null;

         COBieProjectInfo cobieProjectInfo = ExporterCacheManager.ExportOptionsCache.COBieProjectInfo;
         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieProjectInfo != null)
         {
            buildingName = cobieProjectInfo.BuildingName_Number;
            buildingDescription = cobieProjectInfo.BuildingDescription;
         }
         else if (projectInfo != null)
         {
            try
            {
               buildingName = projectInfo.BuildingName;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
            }
            buildingDescription = NamingUtil.GetOverrideStringValue(projectInfo, "BuildingDescription", null);
            buildingLongName = NamingUtil.GetOverrideStringValue(projectInfo, "BuildingLongName", buildingName);
            buildingObjectType = NamingUtil.GetOverrideStringValue(projectInfo, "BuildingObjectType", null);
         }

         IFCFile file = exporterIFC.GetFile();
         IFCAnyHandle address = null;
         if (Exporter.NeedToCreateAddressForBuilding(document))
            address = Exporter.CreateIFCAddress(file, document, projectInfo);

         string buildingGUID = GUIDUtil.CreateProjectLevelGUID(document, GUIDUtil.ProjectLevelGUIDType.Building);
         IFCAnyHandle buildingHandle = IFCInstanceExporter.CreateBuilding(exporterIFC,
             buildingGUID, ownerHistory, buildingName, buildingDescription, buildingObjectType, buildingPlacement, null, buildingLongName,
             Toolkit.IFCElementComposition.Element, null, null, address);
         ExporterCacheManager.BuildingHandle = buildingHandle;

         if (ExporterCacheManager.ExportOptionsCache.ExportAs2x3COBIE24DesignDeliverable && cobieProjectInfo != null)
         {
            string classificationParamValue = cobieProjectInfo.BuildingType;

            if (ClassificationUtil.ParseClassificationCode(classificationParamValue, "dummy",
               out _, out string classificationItemCode, out string classificationItemName) &&
               !string.IsNullOrEmpty(classificationItemCode))
            {
               string relGuidName = classificationItemCode + ":" + classificationItemName;
               string relGuid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssociatesClassification, relGuidName, 
                  buildingHandle));
               ClassificationReferenceKey key = new ClassificationReferenceKey(null, 
                  classificationItemCode, classificationItemName, null, null);
               ExporterCacheManager.ClassificationCache.AddRelation(file, key, relGuid, 
                  "BuildingType", buildingHandle);
            }
         }

         return buildingHandle;
      }

      /// <summary>
      /// Создайте IFCMapConversion, начиная с IFC4, собирая информацию для географической привязки.
      /// </summary>
      /// <param name="exporterIFC">ExporterIFC</param>
      /// <param name="doc">Документ</param>
      /// <param name="geomRepContext">GeometricRepresentationContex</param>
      /// <param name="TNDirRatio">Отношения направления TrueNorth</param>
      private bool ExportIFCMapConversion(ExporterIFC exporterIFC, Document doc, IFCAnyHandle geomRepContext, IList<double> TNDirRatio)
      {
         // Получите информацию из параметров информации о проекте для глобальной системы координат и глобального положения проекта.
         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            return false;

         ProjectInfo projectInfo = doc.ProjectInformation;


         double dblVal = double.MinValue;
         IFCFile file = exporterIFC.GetFile();

         // Объяснение:
         // Точка съемки будет содержать северное и восточное направления известной точки съемки, обычно близкой к проекту.
         // Это относительно ссылки (0,0) используемой системы картографической проекции (EPSG: xxxx)
         // Обычно это можно сделать, сначала найдя точку съемки по географической привязке (0,0) (обычно -x, -y известных координат точки съемки)
         // Затем он возвращается в НЕОБРЕЗАННОМ месте в исходное место.
         // По сути, это создает общее местоположение по ссылке на карту (0,0)

         string epsgCode = null;
         string crsDescription = null;
         string crsGeodeticDatum = null;
         IFCAnyHandle crsMapUnit = null;
         string crsMapUnitStr = null;

         double? scale = null;
         if (ParameterUtil.GetDoubleValueFromElement(projectInfo, "ProjectGlobalPositioning.Scale", out dblVal) != null)
            scale = dblVal;
         string crsVerticalDatum = null;
         string crsMapProjection = null;
         string crsMapZone = null;

         double eastings = 0.0;
         double northings = 0.0;
         double orthogonalHeight = 0.0;
         double? xAxisAbscissa = null;
         double? xAxisOrdinate = null;

         if (ExporterCacheManager.ExportOptionsCache.ExportingSeparateLink())
         {
            if (CoordReferenceInfo.CrsInfo.CrsInfoNotSet)
               return false;

            crsDescription = CoordReferenceInfo.CrsInfo.GeoRefCRSDesc;
            crsGeodeticDatum = CoordReferenceInfo.CrsInfo.GeoRefGeodeticDatum;
            epsgCode = CoordReferenceInfo.CrsInfo.GeoRefCRSName;
            crsMapUnitStr = CoordReferenceInfo.CrsInfo.GeoRefMapUnit;
            crsVerticalDatum = CoordReferenceInfo.CrsInfo.GeoRefVerticalDatum;
            crsMapProjection = CoordReferenceInfo.CrsInfo.GeoRefMapProjection;
            crsMapZone = CoordReferenceInfo.CrsInfo.GeoRefMapZone;

            eastings = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.X;
            northings = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.Y;
            orthogonalHeight = CoordReferenceInfo.MainModelGeoRefOrWCS.Origin.Z;

            xAxisAbscissa = CoordReferenceInfo.MainModelGeoRefOrWCS.BasisX.X;
            xAxisOrdinate = CoordReferenceInfo.MainModelGeoRefOrWCS.BasisX.Y;
         }
         else
         {
            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.VerticalDatum", out crsVerticalDatum) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSVerticalDatum", out crsVerticalDatum);

            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.MapProjection", out crsMapProjection) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSMapProjection", out crsMapProjection);

            if (ParameterUtil.GetStringValueFromElement(projectInfo, "IfcProjectedCRS.MapZone", out crsMapZone) == null)
               ParameterUtil.GetStringValueFromElement(projectInfo, "ProjectGlobalPositioning.CRSMapZone", out crsMapZone);

            //string defaultEPSGCode = "EPSG:3857";     // По умолчанию EPSG:3857, который обычно используется в ProjectedCR, как в GoogleMap, OpenStreetMap.
            crsMapUnitStr = ExporterCacheManager.ExportOptionsCache.GeoRefMapUnit;
            (string projectedCRSName, string projectedCRSDesc, string epsgCode, string geodeticDatum, string uom) crsInfo = (null, null, null, null, null);
            if (string.IsNullOrEmpty(ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode))
            {
               // Только имя CRS является обязательным. Наборы параметров
               // в ProjectInfo переопределяют любое значение, если таковое имеется.
               if (string.IsNullOrEmpty(epsgCode))
               {
                  // Попробуйте получить идентификатор системы координат GIS из SiteLocation.
                  crsInfo = OptionsUtil.GetEPSGCodeFromGeoCoordDef(doc.SiteLocation);
                  if (!string.IsNullOrEmpty(crsInfo.projectedCRSName) && !string.IsNullOrEmpty(crsInfo.epsgCode))
                     epsgCode = crsInfo.epsgCode;

                  crsMapUnitStr = crsInfo.uom;
               }
            }
            else
            {
               epsgCode = ExporterCacheManager.ExportOptionsCache.GeoRefEPSGCode;
            }

            // Нет необходимости создавать IfcMapConversion и IfcProjectedCRS,
            // если не предоставлена информация о географической привязке
            if (string.IsNullOrEmpty(epsgCode))
            {
               // Очищаем всю информацию в CrsInfo, если для MapConversion нечего установить
               CoordReferenceInfo.CrsInfo.Clear();
               return false;
            }

            // IFC only "accepts" EPSG. see https://standards.buildingsmart.org/MVD/RELEASE/IFC4/ADD2_TC1/RV1_2/HTML/schema/ifcrepresentationresource/lexical/ifccoordinatereferencesystem.htm
            if (!epsgCode.StartsWith("EPSG:", StringComparison.InvariantCultureIgnoreCase))
            {
               // Значение может содержать только число, то есть оно EPSG:<the number>
               int epsgNum = -1;
               if (int.TryParse(epsgCode, out epsgNum))
               {
                  epsgCode = "EPSG:" + epsgCode;
               }
            }

            SiteTransformBasis wcsBasis = ExporterCacheManager.ExportOptionsCache.SiteTransformation;
            (double eastings, double northings, double orthogonalHeight, double angleTN, double origAngleTN) geoRefInfo =
                  OptionsUtil.GeoReferenceInformation(doc, wcsBasis, ExporterCacheManager.SelectedSiteProjectLocation);
            eastings = geoRefInfo.eastings;
            northings = geoRefInfo.northings;
            orthogonalHeight = geoRefInfo.orthogonalHeight;

            if (TNDirRatio != null)
            {
               xAxisAbscissa = TNDirRatio.Count > 0 ? TNDirRatio[1] : 0;
               xAxisOrdinate = TNDirRatio.Count > 1 ? TNDirRatio[0] : 0;
            }

            crsDescription = ExporterCacheManager.ExportOptionsCache.GeoRefCRSDesc;
            if (string.IsNullOrEmpty(crsDescription) && !string.IsNullOrEmpty(crsInfo.projectedCRSDesc))
               crsDescription = crsInfo.projectedCRSDesc;
            crsGeodeticDatum = ExporterCacheManager.ExportOptionsCache.GeoRefGeodeticDatum;
            if (string.IsNullOrEmpty(crsGeodeticDatum) && !string.IsNullOrEmpty(crsInfo.geodeticDatum))
               crsGeodeticDatum = crsInfo.geodeticDatum;
         }

         // Обработка блока карты
         ForgeTypeId utId = UnitTypeId.Meters;
         if (!string.IsNullOrEmpty(crsMapUnitStr))
         {
            if (crsMapUnitStr.EndsWith("Metre", StringComparison.InvariantCultureIgnoreCase) || crsMapUnitStr.EndsWith("Meter", StringComparison.InvariantCultureIgnoreCase))
            {
               IFCSIPrefix? prefix = null;
               if (crsMapUnitStr.Length > 5)
               {
                  string prefixStr = crsMapUnitStr.Substring(0, crsMapUnitStr.Length - 5);
                  if (Enum.TryParse(prefixStr, true, out IFCSIPrefix prefixEnum))
                  {
                     prefix = prefixEnum;
                     switch (prefix)
                     {
                        // Обработка единиц СИ от ММ до М. Каким-то образом UnitTypeId
                        // не имеет единиц измерения больше М (в любом случае вряд ли он будет в EPSG)
                        case IFCSIPrefix.Deci:
                           utId = UnitTypeId.Decimeters;
                           break;
                        case IFCSIPrefix.Centi:
                           utId = UnitTypeId.Centimeters;
                           break;
                        case IFCSIPrefix.Milli:
                           utId = UnitTypeId.Millimeters;
                           break;
                        default:
                           utId = UnitTypeId.Meters;
                           break;
                     }
                  }
               }
               crsMapUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.LengthUnit, prefix, IFCSIUnitName.Metre);
            }
            else
            {
               double lengthScaleFactor = 1.0;
               if (crsMapUnitStr.Equals("inch", StringComparison.InvariantCultureIgnoreCase))
               {
                  lengthScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Inches);
                  utId = UnitTypeId.Inches;
               }
               else if (crsMapUnitStr.Equals("foot", StringComparison.InvariantCultureIgnoreCase))
               {
                  lengthScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Feet);
                  utId = UnitTypeId.Feet;
               }

               double lengthSIScaleFactor = UnitUtils.ConvertFromInternalUnits(1.0, UnitTypeId.Meters) / lengthScaleFactor;
               IFCAnyHandle lenDims = IFCInstanceExporter.CreateDimensionalExponents(file, 1, 0, 0, 0, 0, 0, 0); // length
               IFCAnyHandle lenSIUnit = IFCInstanceExporter.CreateSIUnit(file, IFCUnit.LengthUnit, null, IFCSIUnitName.Metre);
               IFCAnyHandle lenConvFactor = IFCInstanceExporter.CreateMeasureWithUnit(file, Toolkit.IFCDataUtil.CreateAsLengthMeasure(lengthSIScaleFactor),
                   lenSIUnit);

               crsMapUnit = IFCInstanceExporter.CreateConversionBasedUnit(file, lenDims, IFCUnit.LengthUnit, crsMapUnitStr, lenConvFactor);
            }
         }

         IFCAnyHandle projectedCRS = null;
         projectedCRS = IFCInstanceExporter.CreateProjectedCRS(file, epsgCode, crsDescription, crsGeodeticDatum, crsVerticalDatum,
            crsMapProjection, crsMapZone, crsMapUnit);


         // Помимо CRSSource (GeometricRepresentationContext) и CRSTarget (ProjectedCRS)
         // обязательными являются только восточные, северные и ортогональные высоты.
         eastings = UnitUtils.ConvertFromInternalUnits(eastings, utId);
         northings = UnitUtils.ConvertFromInternalUnits(northings, utId);
         orthogonalHeight = UnitUtils.ConvertFromInternalUnits(orthogonalHeight, utId);
         IFCAnyHandle mapConversionHnd = IFCInstanceExporter.CreateMapConversion(file, geomRepContext, projectedCRS, eastings, northings,
            orthogonalHeight, xAxisAbscissa, xAxisOrdinate, scale);

         // Назначаем информацию MapConversion основной модели
         if (ExporterUtil.ExportingHostModel() && 
            !ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            RememberWCSOrGeoReference(eastings, northings, orthogonalHeight, xAxisAbscissa ?? 1.0,
               xAxisOrdinate ?? 0.0);
            CoordReferenceInfo.CrsInfo.GeoRefCRSName = epsgCode;
            CoordReferenceInfo.CrsInfo.GeoRefCRSDesc = crsDescription;
            CoordReferenceInfo.CrsInfo.GeoRefGeodeticDatum = crsGeodeticDatum;
            CoordReferenceInfo.CrsInfo.GeoRefVerticalDatum = crsVerticalDatum;
            CoordReferenceInfo.CrsInfo.GeoRefMapUnit = crsMapUnitStr;
            CoordReferenceInfo.CrsInfo.GeoRefMapProjection = crsMapProjection;
            CoordReferenceInfo.CrsInfo.GeoRefMapZone = crsMapZone;
         }

         return true;
      }

      private bool RememberWCSOrGeoReference (double eastings, double northings, double orthogonalHeight, double xAxisAbscissa, double xAxisOrdinate)
      {
         Transform wcsOrGeoRef = Transform.Identity;
         wcsOrGeoRef.Origin = new XYZ(UnitUtil.UnscaleLength(eastings), UnitUtil.UnscaleLength(northings), UnitUtil.UnscaleLength(orthogonalHeight));
         wcsOrGeoRef.BasisX = new XYZ(xAxisAbscissa, xAxisOrdinate, 0.0);
         wcsOrGeoRef.BasisZ = new XYZ(0.0, 0.0, 1.0);
         wcsOrGeoRef.BasisY = wcsOrGeoRef.BasisZ.CrossProduct(wcsOrGeoRef.BasisX);
         CoordReferenceInfo.MainModelGeoRefOrWCS = wcsOrGeoRef;

         return true;
      }

      /// <summary>
      /// Создать IFCSystem из кэшированных элементов
      /// </summary>
      /// <param name="exporterIFC">Объект экспорта IFC.</param>
      /// <param name="doc">Документ для экспорта.</param>
      /// <param name="file">Файл IFC.</param>
      /// <param name="systemsCache">Экспортируемые системы.</param>
      /// <param name="ownerHistory">История владельца.</param>
      /// <param name="buildingHandle">Дескриптор здания.</param>
      /// <param name="projectHasBuilding">Существует ли здание.</param>
      /// <param name="isElectricalSystem"> Является ли система электрической.</param>
      private bool ExportCachedSystem(ExporterIFC exporterIFC, Document doc, IFCFile file, IDictionary<ElementId, ISet<IFCAnyHandle>> systemsCache,
         IFCAnyHandle ownerHistory, IFCAnyHandle buildingHandle, bool projectHasBuilding, bool isElectricalSystem)
      {
         bool res = false;
         IDictionary<string, Tuple<string, HashSet<IFCAnyHandle>>> genericSystems = new Dictionary<string, Tuple<string, HashSet<IFCAnyHandle>>>();

         foreach (KeyValuePair<ElementId, ISet<IFCAnyHandle>> system in systemsCache)
         {
            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
               MEPSystem systemElem = doc.GetElement(system.Key) as MEPSystem;
               if (systemElem == null)
                  continue;

               Element baseEquipment = systemElem.BaseEquipment;
               if (baseEquipment != null)
               {
                  IFCAnyHandle memberHandle = ExporterCacheManager.MEPCache.Find(baseEquipment.Id);
                  if (!IFCAnyHandleUtil.IsNullOrHasNoValue(memberHandle))
                     system.Value.Add(memberHandle);
               }

               if (isElectricalSystem)
               {
                  // Свойство Elements ниже может в некоторых случаях вызывать исключение InvalidOperationException, что может
                  // сбой экспорта. Защититесь от этого, не используя слишком общий блок try/catch.
                  try
                  {
                     ElementSet members = systemElem.Elements;
                     foreach (Element member in members)
                     {
                        IFCAnyHandle memberHandle = ExporterCacheManager.MEPCache.Find(member.Id);
                        if (!IFCAnyHandleUtil.IsNullOrHasNoValue(memberHandle))
                           system.Value.Add(memberHandle);
                     }
                  }
                  catch
                  {
                  }
               }

               if (system.Value.Count == 0)
                  continue;

               ElementType systemElemType = doc.GetElement(systemElem.GetTypeId()) as ElementType;
               string name = NamingUtil.GetNameOverride(systemElem, systemElem.Name);
               string desc = NamingUtil.GetDescriptionOverride(systemElem, null);
               string objectType = NamingUtil.GetObjectTypeOverride(systemElem,
                   (systemElemType != null) ? systemElemType.Name : "");

               string systemGUID = GUIDUtil.CreateGUID(systemElem);
               IFCAnyHandle systemHandle = null;
               if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
               {
                  systemHandle = IFCInstanceExporter.CreateSystem(file, systemGUID,
                     ownerHistory, name, desc, objectType);
               }
               else
               {
                  string longName = NamingUtil.GetLongNameOverride(systemElem, null);
                  string ifcEnumType;
                  IFCExportInfoPair exportAs = ExporterUtil.GetObjectExportType(exporterIFC, systemElem, out ifcEnumType);

                  string predefinedType = exportAs.ValidatedPredefinedType;
                  if (predefinedType == null)
                  {
                     Toolkit.IFC4.IFCDistributionSystem systemType = ConnectorExporter.GetMappedIFCDistributionSystemFromElement(systemElem);
                     predefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(systemType.ToString());
                  }

                  if (exportAs.ExportInstance == IFCEntityType.IfcDistributionCircuit)
                  {
                     systemHandle = IFCInstanceExporter.CreateDistributionCircuit(file, systemGUID,
                        ownerHistory, name, desc, objectType, longName, predefinedType);

                     string systemName;
                     ParameterUtil.GetStringValueFromElementOrSymbol(systemElem, "IfcDistributionSystem", out systemName);

                     if (!string.IsNullOrEmpty(systemName) && systemHandle != null)
                     {
                        Tuple<string, HashSet<IFCAnyHandle>> circuits = null;
                        if (!genericSystems.TryGetValue(systemName, out circuits))
                        {
                           circuits = new Tuple<string, HashSet<IFCAnyHandle>>(null, new HashSet<IFCAnyHandle>());
                           genericSystems[systemName] = circuits;
                        }

                        // Чтение PredefinedType для общей системы
                        if (string.IsNullOrEmpty(circuits.Item1))
                        {
                           string genericPredefinedType;
                           ParameterUtil.GetStringValueFromElementOrSymbol(systemElem, "IfcDistributionSystemPredefinedType", out genericPredefinedType);
                           genericPredefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(genericPredefinedType);
                           if (!string.IsNullOrEmpty(genericPredefinedType))
                              genericSystems[systemName] = new Tuple<string, HashSet<IFCAnyHandle>>(genericPredefinedType, circuits.Item2);
                        }
                        // Добавляем схему в общую систему
                        circuits.Item2.Add(systemHandle);

                     }
                  }
                  else
                  {
                     systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                       ownerHistory, name, desc, objectType, longName, predefinedType);
                  }

               }

               if (systemHandle == null)
                  continue;
               res = true;

               // Создать ссылку на классификацию, когда системе присвоено имя файла классификации
               ClassificationUtil.CreateClassification(exporterIFC, file, systemElem, systemHandle);

               productWrapper.AddSystem(systemElem, systemHandle);

               if (projectHasBuilding)
               {
                  CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);
               }

               IFCObjectType? objType = null;
               if (!ExporterCacheManager.ExportOptionsCache.ExportAsCoordinationView2 && ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
                  objType = IFCObjectType.Product;
               string groupGuid = GUIDUtil.GenerateIFCGuidFrom(
                  GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, systemHandle));
               IFCAnyHandle relAssignsToGroup = IFCInstanceExporter.CreateRelAssignsToGroup(file,
                  groupGuid, ownerHistory, null, null, system.Value, objType, systemHandle);

               ExporterUtil.ExportRelatedProperties(exporterIFC, systemElem, productWrapper);
            }
         }

         foreach (KeyValuePair<string, Tuple<string, HashSet<IFCAnyHandle>>> system in genericSystems)
         {
            string systemGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcDistributionSystem, system.Key));
            IFCAnyHandle systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                       ownerHistory, system.Key, null, null, null, system.Value.Item1);
            if (systemHandle == null)
               continue;
            
            if (projectHasBuilding)
               CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);

            string relGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAggregates, systemHandle));
            IFCInstanceExporter.CreateRelAggregates(file, relGUID, ownerHistory, 
               null, null, systemHandle, system.Value.Item2);
         }

         return res;
      }

      /// <summary>
      /// Создание систем на основе общего параметра IfcSystem 
      /// кэшированных элементов кабельных лотков и кабелепроводов.
      /// </summary>
      /// <param name="doc">Документ для экспорта.</param>
      /// <param name="file">Файл IFC.</param>
      /// <param name="elementsCache">Экспортируемые системы.</param>
      /// <param name="ownerHistory">История владельца.</param>
      /// <param name="buildingHandle">Дескриптор здания.</param>
      /// <param name="projectHasBuilding">Существует ли здание.</param>
      private bool ExportCableTraySystem(Document doc, IFCFile file, ISet<ElementId> elementsCache,
         IFCAnyHandle ownerHistory, IFCAnyHandle buildingHandle, bool projectHasBuilding)
      {
         bool res = false;

         // группировать элементы по строковому параметру IfcSystem
         IDictionary<string, ISet<IFCAnyHandle>> groupedSystems = new Dictionary<string, ISet<IFCAnyHandle>>();
         foreach (ElementId elemeId in elementsCache)
         {
            IFCAnyHandle handle = ExporterCacheManager.MEPCache.Find(elemeId);
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(handle))
               continue;

            string systemName;
            Element elem = doc.GetElement(elemeId);
            ParameterUtil.GetStringValueFromElementOrSymbol(elem, "IfcSystem", out systemName);
            if (string.IsNullOrEmpty(systemName))
               continue;

            ISet<IFCAnyHandle> systemElements;
            if (!groupedSystems.TryGetValue(systemName, out systemElements))
            {
               systemElements = new HashSet<IFCAnyHandle>();
               groupedSystems[systemName] = systemElements;
            }
            systemElements.Add(handle);
         }

         // экспортируем системы
         foreach (KeyValuePair<string, ISet<IFCAnyHandle>> system in groupedSystems)
         {
            string systemGUID = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcSystem, system.Key));
            IFCAnyHandle systemHandle = null;
            if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               systemHandle = IFCInstanceExporter.CreateSystem(file, systemGUID,
                  ownerHistory, system.Key, "", "");
            }
            else
            {
               Toolkit.IFC4.IFCDistributionSystem systemType = Toolkit.IFC4.IFCDistributionSystem.NOTDEFINED;
               string predefinedType = IFCValidateEntry.ValidateStrEnum<Toolkit.IFC4.IFCDistributionSystem>(systemType.ToString());

               systemHandle = IFCInstanceExporter.CreateDistributionSystem(file, systemGUID,
                  ownerHistory, system.Key, "", "", "", predefinedType);
            }

            if (systemHandle == null)
               continue;

            if (projectHasBuilding)
               CreateRelServicesBuildings(buildingHandle, file, ownerHistory, systemHandle);

            IFCObjectType? objType = null;
            if (!ExporterCacheManager.ExportOptionsCache.ExportAsCoordinationView2 && 
               ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
               objType = IFCObjectType.Product;
            string relAssignsGuid = GUIDUtil.GenerateIFCGuidFrom(
               GUIDUtil.CreateGUIDString(IFCEntityType.IfcRelAssignsToGroup, systemHandle));
            IFCInstanceExporter.CreateRelAssignsToGroup(file, relAssignsGuid,
               ownerHistory, null, null, system.Value, objType, systemHandle);
            res = true;
         }
         return res;
      }

      /// <summary>
      /// Принудительно выполнить немедленную сборку мусора.
      /// </summary>
      /// <remarks>
      /// Освобождение памяти, занятой объектами, у которых реализованы финализаторы
      /// требуется два прохода, поскольку такие объекты помещаются в очередь финализации
      /// вместо того, чтобы быть очищенным при первом проходе при запуске сборщика мусора.
      /// </remarks>
      private void ForceGarbageCollection()
      {
         GC.Collect();
         GC.WaitForPendingFinalizers();
         GC.Collect();
         GC.WaitForPendingFinalizers();
      }
   }
}
