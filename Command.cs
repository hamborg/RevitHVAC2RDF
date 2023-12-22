using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.ApplicationServices;
//using RestSharp;
using System.Net.Http;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using System.IO;
using System.Dynamic;
using System.Windows.Media;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.CodeDom;
using Newtonsoft.Json.Linq;

namespace RevitToRDFConverter
{

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]

    public class Command : IExternalCommand
    {
        /***** VARIABLES *****/

        // Instansiate doc
        public Document doc = null;
        // Instansiate string for triple-creations:
        public StringBuilder sb = new StringBuilder();
        public StringBuilder tester = new StringBuilder();


        // List of available systems - i.e. the only ones we care about extracting components from:
        public IList<Element> ventilationComponents = new List<Element>();
        public IList<Element> pipingComponents = new List<Element>();


        // All Guids and RDF-id's into dictionary:
        public Dictionary<string, string> ID = new Dictionary<string, string>();
        
        // All entities (subjects AND objects) into object:
        public dynamic E = new ExpandoObject(); // Skal måske udelades


        //////////////////////////////////////////////////
        /***** METHODS *****/

        // ******************** TOOLS ******************** //
        public void AddBreak(string title = "NEW RDF SEGMENT") // LHAM: indskudte sektioner i RDF (for læsbarhed).
        {
            sb.Append($"\n### --- {title} --- ###\n");
        }
        public void PutInDictonary(string key, string value)
        { // Rule: Key = Guid, Value = RDF id
            // NOTE: (Indsæt logik, der tjekker, om key findes allerede.)

            ID[key] = value;
        }
        public string GetIfcGuid(Element e)
        {
            Parameter parameter = e.get_Parameter(BuiltInParameter.IFC_GUID);
            return parameter.AsValueString();
        }
        public string GetRdfInstFromId(ElementId id)
        {
            if (id.IntegerValue < 0) return null;
            /*{
                Element element = doc.GetElement(id);
                switch(element.GetType().Name)
                {
                    case ("Duct Insulations"):
                        DuctInsulation insulation = (DuctInsulation)element;
                        id = insulation.HostElementId;
                        tester.Append("\n Insulation: " + id);
                        break;
                    case ("Duct Linings"):
                        DuctLining lining = (DuctLining)element;
                        id = lining.HostElementId;
                        tester.Append("\n Lining: " + id);
                        break;
                    default:
                        return "NO_GUID";
                }
            }*/

            string elementGuid = doc.GetElement(id).UniqueId;
            return ID[elementGuid];
        }
        
        int counter = 0; // ************* Slet mig igen, når SkipComp. er færdigt *************
        public bool SkipComponentCategory(Element component)
        {
            // do smthg clever...?

            //if(counter == 0) tester.Append("\n\n ### Skip Component ###\n");
            bool skipComponent = false;
            switch (component.Category.Name)
            {
                case ("Duct Insulations"):
                case ("Duct Linings"):
                case ("Air Terminals"):
                    //tester.Append($"\n {counter} Skippes:" + component.Category.Name);
                    skipComponent = true;
                    // (Gør noget andet: e.g. sæt som property på component)
                    break;
                case ("Ducts"):
                case ("Duct Fittings"):
                case ("Duct Placeholders"):
                //skipComponent = true; break;
                //case ("Air Terminals"):
                case ("Duct Accessories"):
                case ("Mechanical Equipment"):
                case ("Flex Ducts"):
                case ("Mechanical Control Devices"):
                case ("Mechanical Fabrication Parts"): // LHAM: Har ikke testet navn
                case ("MEP Fabrication Parts"): // LHAM: Har ikke testet navn
                    //tester.Append($"\n {counter} Bruges:" + component.Category.Name);
                    break;
                default:
                    //tester.Append($"\n {counter} Default:" + component.Category.Name);
                    skipComponent = true;
                    break;
            }
            counter++;
            //return skipComponent;
            return false;
        }
        public string GetFamilyAndTypeName(Element component, int c = 1)
        {
            ElementId typeId = component.GetTypeId();
            ElementType typeElement = doc.GetElement(typeId) as ElementType;

            if (typeElement == null) return "(No family information)";

            if (c == 0) return typeElement.FamilyName;  // Main Family
            if (c == 1) return typeElement.Name;        // Family Type Name
            else        return "WRONG INPUT";
        }
        public XYZ ConvertToMM(XYZ xyz, bool round = true)
        {
            double x;
            double y;   
            double z;
            if (round)
            {
                x = Math.Round(UnitUtils.ConvertFromInternalUnits(xyz.X, UnitTypeId.Millimeters));
                y = Math.Round(UnitUtils.ConvertFromInternalUnits(xyz.Y, UnitTypeId.Millimeters));
                z = Math.Round(UnitUtils.ConvertFromInternalUnits(xyz.Z, UnitTypeId.Millimeters));
            }
            else
            {
                x = UnitUtils.ConvertFromInternalUnits(xyz.X, UnitTypeId.Millimeters);
                y = UnitUtils.ConvertFromInternalUnits(xyz.Y, UnitTypeId.Millimeters);
                z = UnitUtils.ConvertFromInternalUnits(xyz.Z, UnitTypeId.Millimeters);
            }

            return new XYZ(x, y, z);
        }



        // ******************** ELEMENTS EXTRACTORS ******************** //
        public FilteredElementCollector ElementCollector<T>()
        {

            string test = "hov";
            test = "test";
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ICollection<Element> elements = collector.OfClass(typeof(T)).ToElements();
            //List<T> elementList = new List<T>();                                      // Levn... Hvad skal det bruges til?

            return collector;
        }
        public void BuildingExtractor()
        {
            AddBreak("BUILDING(S)");

            // Create new object for the building:
            dynamic Bldg = new ExpandoObject();

            // ***** Collect RDF data:
            // ID data
            ProjectInfo projectInfo = doc.ProjectInformation;
            Bldg.NameId = "Bldg_" + "MainBuilding"; // Until better idea of unique building-identifier
            Bldg.Name = projectInfo.BuildingName;
            Bldg.RevitId = projectInfo.Id.ToString();
            Bldg.RevitGuid = projectInfo.UniqueId.ToString();

            // ***** Write RDF triples:
            sb.Append(""
                + $"inst:{Bldg.NameId} a bot:Building . \n"
                + $"inst:{Bldg.NameId} rdfs:label \"{Bldg.Name}\"^^xsd:string  . \n"
                + $"inst:{Bldg.NameId} rvt:id \"{Bldg.RevitId}\"^^xsd:string  . \n"
                + $"inst:{Bldg.NameId} rvt:guid \"{Bldg.RevitGuid}\"^^xsd:string  . \n"
                );

            // Add Bldg object to Entities object:
            E.Bldg = Bldg;

            // Add GUID and RDF id to ID Dictonary:
            PutInDictonary(Bldg.RevitGuid, Bldg.NameId);
        }
        public void LevelExtractor()
        {
            AddBreak("LEVELS");

            // Create a list of all levels:
            E.levels = new List<dynamic>();

            foreach (Level level in ElementCollector<Level>() )
            {
                // Create new object for the level:
                dynamic Lvl = new ExpandoObject();

                // ***** Collect RDF data:
                // ID data
                Lvl.RevitId = level.Id.ToString();
                Lvl.RevitGuid = level.UniqueId.ToString();
                Lvl.IfcGuid = GetIfcGuid(level);
                Lvl.Name = level.Name;
                Lvl.NameId = "Lvl_" + Lvl.Name.Replace(" ", "-");

                // ***** Write RDF triples:
                sb.Append(""
                    + $"inst:{E.Bldg.NameId} bot:hasStorey inst:{Lvl.NameId} . \n"
                    + $"inst:{Lvl.NameId} a bot:Storey . \n"
                    + $"inst:{Lvl.NameId} rdfs:label \"{Lvl.Name}\"^^xsd:string . \n"
                    + $"inst:{Lvl.NameId} rvt:id \"{Lvl.RevitId}\"^^xsd:string . \n"
                    + $"inst:{Lvl.NameId} rvt:guid \"{Lvl.RevitGuid}\"^^xsd:string . \n"
                    + $"inst:{Lvl.NameId} rvt:ifcGuid \"{Lvl.IfcGuid}\"^^xsd:string . \n"
                    + "\n");


                // Add Level to Levels list in Entities object:
                E.levels.Add( Lvl );

                // Add GUID and RDF id to ID Dictonary:
                PutInDictonary(Lvl.RevitGuid, Lvl.NameId);
            }
        }
        public void SpaceExtractor()
        {
            // BuiltInCategory: OST_MEPSpaces
            // Space.Name
            // Space.Number
            // Space.Level.UniqueId

            // Space.Area



            // **** ORG ALI-KODE: *****

            /*          Spaces
                        AddBreak(sb, "SPACES");
                        //            //Get all spaces and the level they are to. WORKING (... Not working with links!)
                        FilteredElementCollector roomCollector = new FilteredElementCollector(doc);
                        ICollection<Element> rooms = roomCollector.OfClass(typeof(SpatialElement)).ToElements();
                        List<SpatialElement> roomList = new List<SpatialElement>();
                        foreach (SpatialElement space in roomCollector)
                        {
                            //SpatialElement w = space as SpatialElement;
                            if (space.Category.Name == "Spaces" & space.LookupParameter("Area").AsDouble() > 0)
                            {
                                string spaceName = space.Name.Replace(' ', '-');
                                string spaceGuid = space.UniqueId.ToString();
                                string isSpaceOf = space.Level.UniqueId;

                                string designCoolingLoadID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                double designCoolingLoad = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Design Cooling Load").AsDouble(), UnitTypeId.Watts);

                                string designHeatingLoadID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                double designHeatingLoad = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Design Heating Load").AsDouble(), UnitTypeId.Watts);

                                string designSupplyAirflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                double designSupplyAirflow = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Actual Supply Airflow").AsDouble(), UnitTypeId.LitersPerSecond);

                                string designReturnAirflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                double designReturnAirflow = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Actual Return Airflow").AsDouble(), UnitTypeId.LitersPerSecond);

                                sb.Append($"inst:{spaceGuid} a bot:Space . \n" +
                                    $"inst:{spaceGuid} rdfs:label '{spaceName}'^^xsd:string . \n" +
                                    $"inst:{isSpaceOf} bot:hasSpace inst:{spaceGuid} . \n" +

                                    $"#Cooling Demand in {spaceName}" + "\n" +
                                    $"inst:{spaceGuid} ex:hasDesignCoolingDemand inst:{designCoolingLoadID} . \n" +
                                    $"inst:{designCoolingLoadID} a ex:DesignCoolingDemand . \n" +
                                    $"inst:{designCoolingLoadID} fpo:hasValue '{designCoolingLoad}'^^xsd:double . \n" +
                                    $"inst:{designCoolingLoadID} fpo:hasUnit 'Watts'^^xsd:string . \n" +

                                    $"#Heating Demand in {spaceName}" + "\n" +
                                    $"inst:{spaceGuid} ex:hasDesignHeatingDemand inst:{designHeatingLoadID} . \n" +
                                    $"inst:{designHeatingLoadID} a ex:DesignHeatingDemand . \n" +
                                    $"inst:{designHeatingLoadID} fpo:hasValue '{designHeatingLoad}'^^xsd:double . \n" +
                                    $"inst:{designHeatingLoadID} fpo:hasUnit 'Watts'^^xsd:string . \n" +

                                    // LHAM: I disse to blokke, var prefix sat til "ice:" hvilket gav fejl. Disse er rettet til "ex:"
                                    $"#Supply Air Flow Demand in {spaceName}" + "\n" +
                                    $"inst:{spaceGuid} ex:hasDesignSupplyAirflowDemand inst:{designSupplyAirflowID} . \n" +
                                    $"inst:{designSupplyAirflowID} a ex:DesignSupplyAirflowDemand . \n" +
                                    $"inst:{designSupplyAirflowID} fpo:hasValue '{designSupplyAirflow}'^^xsd:double . \n" +
                                    $"inst:{designSupplyAirflowID} fpo:hasUnit 'Liters Per Second'^^xsd:string . \n" +

                                    $"#Return Air Flow Demand in {spaceName}" + "\n" +
                                    $"inst:{spaceGuid} ex:hasDesignReturnAirflowDemand inst:{designReturnAirflowID} . \n" +
                                    $"inst:{designReturnAirflowID} a ex:DesignReturnAirflowDemand . \n" +
                                    $"inst:{designReturnAirflowID} fpo:hasValue '{designReturnAirflow}'^^xsd:double . \n" +
                                    $"inst:{designReturnAirflowID} fpo:hasUnit 'Liters Per Second'^^xsd:string . \n"
                                );
                            };
                        }
            */
        } // Undone
        public void VentSystemExtractor()
        {
            AddBreak("VENT SYSTEMS");

            foreach (MechanicalSystem system in ElementCollector<MechanicalSystem>())
            {
                // ***** Extract data and put into the entities object @E.vs ("V"entilation "S"ystem):
                //E.vs = new ExpandoObject(); // (Droppede det igen: "Hvad skal det bruges til", "skal lave en ny liste for hvert element i 'foreach' loop...")

                // ***** Collect RDF data:
                // ID data
                string systemName       = system.Name; // System abbriviation - made by modeller
                string systemRevitId    = system.Id.ToString();
                string systemRevitGUID  = system.UniqueId;
                string systemIfcGUID    = GetIfcGuid(system);
                string systemNameId     = "VentSys_" + systemName.Replace(" ", "-");
                // Revit family data
                string systemCategory           = system.Category.Name;
                string systemFamily             = GetFamilyAndTypeName(system, 0);
                string systemFamilyTypeName     = GetFamilyAndTypeName(system);
                // Characteristics and properties
                DuctSystemType systemType       = system.SystemType;
                string systemTypeString         = systemType.ToString(); // Returns: "SupplyAir", "ReturnAir", or "ExhaustAir"


                AddBreak($"Vent system {systemName}");
                
                // Write RDF triples:
                sb.Append($"inst:{systemNameId} rdf:type fso:System, ");
                if      (systemType == DuctSystemType.SupplyAir)  sb.Append("fso:SupplySystem . \n");
                else if (systemType == DuctSystemType.ReturnAir)  sb.Append("fso:ReturnSystem . \n");
                else if (systemType == DuctSystemType.ExhaustAir) sb.Append("fso:ReturnSystem . \n");
                else sb.Append($"ex:{systemTypeString}System . \n"); // Fallback. Shouldn't occur.
                
                sb.Append(""
                    + $"inst:{systemNameId} rdfs:label \"{systemName}\"^^xsd:string . \n"
                    + $"inst:{systemNameId} rvt:id \"{systemRevitId}\"^^xsd:string. \n"
                    + $"inst:{systemNameId} rvt:guid \"{systemRevitGUID}\"^^xsd:string . \n"
                    + $"inst:{systemNameId} rvt:ifcGuid \"{systemIfcGUID}\"^^xsd:string . \n"
                    + $"inst:{systemNameId} rvt:category \"{systemCategory}\"^^xsd:string . \n" // "Duct systemS"
                    + $"inst:{systemNameId} rvt:family \"{systemFamily}\"^^xsd:string . \n" // "Duct system"
                    + $"inst:{systemNameId} rvt:familyType \"{systemFamilyTypeName}\"^^xsd:string . \n"
                    + $"inst:{systemNameId} rvt:systemType \"{systemTypeString}\"^^xsd:string . \n"
                    );


                // ********** System Properties
                // LHAM: Udeladt. Properties skal ligge på componenterne - ikke på systems.


                // Add GUID and RDF id to ID Dictonary:
                PutInDictonary(systemRevitGUID, systemNameId);


                // ********** System Components
                AddBreak($"Components in ventilation system {systemName}");
                ComponentExtractor(system, systemName, systemNameId);

            }

        }
        public void ComponentExtractor(PipingSystem system, string systemName, string systemNameId)
        { 
            // Overload:
            // Repeat method - bare for piping
        } // Undone
        public void ComponentExtractor(MechanicalSystem system, string systemName, string systemNameId)
        {
            // Elements in the system:
            ElementSet network = system.DuctNetwork;
            
            
            // ********** TESTER DUCTS MED TAPS!!! **********

            // Pick out ducts with taps. Add to new list:
            List<Element> networkElements = new List<Element>();
            foreach (Element component in network)
            {

                // ***** LHAM: Skal putte alt det nedenstående i sin egen method,
                // ***** for at kunne gøre det løbende med del-ducts, når de bliver 'klippet' op.
                //
                // ***** Lige nu har vi blot en tro kopi af ducts med taps. De vil få samme id...

                // ***** LHAM:
                // ***** Vi skal også tage højde for flere taps på én duct.
                // ***** Vi skal sortere efter position, på en eller anden måde.

                /* NOTES:
                 * - Componets bliver (vist) løbet igennem ift. creation-tidspunkt.
                 * - Om ikke andet lader de til at blive sorteret (loopet igennem) efter ID.
                 * - Tap-location er et punkt: Ligger på kanal-væg (radius væk fra centerlinje).
                 *   Tee's har location på kanal-midterlinjen
                 * - Tilstødende kanal starter tap-længde (20 mm) væk fra hovedkanal.
                 */



                // Add every component to list of manipulated network:
                networkElements.Add(component);

                // (Do something with the taps and ducts with taps...)
                if (component.Category.Name == "Duct Fittings")
                {

                    tester.Append($"\n{GetFamilyAndTypeName(component)}");

                    string tapLength = doc.GetElement(component.Id).LookupParameter("Takeoff Fixed Length").AsValueString(); // Det gælder vel ikke altid...

                    LocationPoint fittingLocation = (LocationPoint)((FamilyInstance)component).Location;
                    XYZ xyzFitting = ConvertToMM(fittingLocation.Point);

                    
                    tester.Append($": ({xyzFitting.X}, {xyzFitting.Y}) - Length: {tapLength}");
                    
                    
                    
                    FamilyInstance fitting = (FamilyInstance)component;
                    string fittingType = ((MechanicalFitting)fitting.MEPModel).PartType.ToString();
                    if (fittingType == "TapAdjustable")
                    {
                        // What do we do?...
                    }
                }

                if (component.Category.Name == "Ducts")
                {
                    int connecters = RelatedPorts.CountDuctConnectors(component);
                    if (connecters > 2)
                    {
                        tester.Append($"\n{GetFamilyAndTypeName(component)}");

                        LocationCurve ductLocation = (LocationCurve)((Duct)component).Location;
                        IList<XYZ> xyzDucts = ductLocation.Curve.Tessellate();
                        foreach(XYZ xyzDuct in xyzDucts)
                        {
                            XYZ xyz = ConvertToMM(xyzDuct);
                            tester.Append($"\n --- {xyzDucts.IndexOf(xyz)}: ({xyz.X}, {xyz.Y})");
                        }

                        tester.Append($"\n --- TAPCONNECTORS:");
                        networkElements.Add(component);

                        IList<XYZ> connectorLocations = RelatedPorts.GetConnectorLocations(component);
                        foreach(XYZ connectorLocation in connectorLocations)
                        {
                            tester.Append($"\n ------ {connectorLocations.IndexOf(connectorLocation)}: " +
                                $"({connectorLocation.X}, {connectorLocation.Y})");
                        }
                    }
                }
            }



            /*
            
            /////////////////////////////////////////////////////////////
            // ********** OPRINDELIG PARSER, DER VIRKER **********
            // ********** (kommenteret for at teste overstående) **********


            AddBreak($"Component---Extractor for system {systemName}");
            systemName = systemName.Replace(" ", "-");

            // Iterate through components in system:
            foreach (Element component in networkElements) // network
            {
                // Skip elements that are not important for system. E.g. insulation:
                //if (SkipComponentCategory(component)) continue;

                // Add component to list of components (for later property extraction):
                ventilationComponents.Add(component);


                // ***** Collect RDF data:
                // ID data
                string componentRevitID     = component.Id.ToString();
                string componentRevitGuid   = component.UniqueId;
                string componentIfcGuid     = GetIfcGuid(component);
                string componentNameID      = systemName + "_Comp_" + componentRevitID;
                // Revit family data
                string componentCategory        = component.Category.Name;
                string componentFamily          = GetFamilyAndTypeName(component, 0);
                string componentFamilyTypeName  = GetFamilyAndTypeName(component);


                // ***** Write RDF triples:
                sb.Append(""
                    + $"inst:{componentNameID} rdf:type fso:Component . \n"
                    + $"inst:{systemNameId} fso:hasComponent inst:{componentNameID} . \n"
                    + $"inst:{componentNameID} fso:isComponentOf inst:{systemNameId} . \n"
                    + $"inst:{componentNameID} rvt:id \"{componentRevitID}\"^^xsd:string . \n"
                    + $"inst:{componentNameID} rvt:guid \"{componentRevitGuid}\"^^xsd:string . \n"
                    + $"inst:{componentNameID} rvt:ifcGuid \"{componentIfcGuid}\"^^xsd:string . \n"
                    + $"inst:{componentNameID} rvt:category \"{componentCategory}\"^^xsd:string . \n"
                    + $"inst:{componentNameID} rvt:family \"{componentFamily}\"^^xsd:string . # Family \n"
                    + $"inst:{componentNameID} rvt:familyType \"{componentFamilyTypeName}\"^^xsd:string . # Label / Family-Type \n"
                    + "\n");

                // Add GUID and RDF id to ID Dictonary:
                PutInDictonary(componentRevitGuid, componentNameID);


                // NOTE: Vi mangler stadigt at få connectors ind.
                // Men der er endnu ikke taget højde for ducts med taps.
            }
        
             
             */

        } // Undone



        // ******************** PROPERTIES EXTRACTORS ******************** //
        
        // ***** PIPING ***** //
        public void PipingComponentProperties(List<Element> components)
        {

            AddBreak("FSC PIPING COMPONTENTS");

            foreach (Element component in components)
            {
                // Get RDF ID of component:
                string componentID = ID[component.UniqueId];

                // Fetch 'FSC_type' parameter from type:
                Element componentType = doc.GetElement(component.GetTypeId());
                string fscType = componentType.LookupParameter("FSC_type").AsValueString(); // VIRKER

                string fsoClass = null;
                if (fscType?.Length > 0) fsoClass = fscType;

                // ***** Conduct logics and extractions:
                switch (component.Category.Name)
                {
                    case ("Ducts"):
                        tester.Append("\n SWITCH: duct ");
                        fsoClass = "Segment";
                        break;
                }

                //if (component.Category.Name == "Pipe Fittings")
                //  {
                //  string fittingType = ((MechanicalFitting)component.MEPModel).PartType.ToString();
                //  sb.Append($"inst:{componentID} a fso:{fittingType} . \n");

                //  if (fittingType.ToString() == "Tee")
                //  if (fittingType.ToString() == "Elbow")
                //      fpo:Angle
                //  if (fittingType.ToString() == "Transition")
                //      fpo:Length  diameters/cross section areas
                //  if (fittingType.ToString() == "TapReduction") // New

            }
        }

        // ***** VENTILATION ***** //
        public void VentilationFamilyInstanceProperties(Element element)
        {
            // Convert to FamilyInstance:
            FamilyInstance component = (FamilyInstance)element;
            
            // Get component RDF id:
            string componentID = ID[component.UniqueId];

            // Zone of family
            Space containingZone = component.Space;
            //string spaceID = ID[containingSpace.UniqueId];
            sb.Append(""
                + $"{containingZone.ToString()} bot:hasElement {componentID}"
                + $"{componentID} bot:hasElement {containingZone.ToString()}"
                );

            // Specific properties:
            switch (component.Category.Name)
            {
                case ("Duct Fittings"):
                    tester.Append("\n SWITCH: fitting");

                    MechanicalFitting fittingType = (MechanicalFitting)component.MEPModel;
                    PartType fittingPartType = fittingType.PartType;

                    //  sb.Append($"inst:{componentID} a fso:{fittingType} . \n");

                    //  if (fittingPartType == "Tee")

                    //  if (fittingPartType == "Elbow")
                    //      double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                    //      fpo:Angle
                    //+$"inst:{angleID} a fpo:Angle . \n"
                    //+ $"inst:{angleID} fpo:hasValue  '{angleValue}'^^xsd:double . \n"
                    //+ $"inst:{angleID} fpo:hasUnit  'Degree'^^xsd:string . \n");

                    //  if (fittingPartType == "Transition")
                    //double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("OffsetHeight").AsDouble(), UnitTypeId.Meters) // VIRKER FORKERT MED OFFSET HEIGHT!
                    //      fpo:Length  diameters/cross section areas

                    //  if (fittingPartType == "TapAdjustable") // New
                    //      // Klip duct op og indsæt Tee - ny function!

                    break;

                case ("Mechanical Equipment"):
                    tester.Append("\n SWITCH: MEqm");
                    break;

                case ("Air Terminals"):
                    tester.Append("\n SWITCH: AT");
                    
                    // Air Terminal Type:
                    string airTerminalTypeID = "atType_" + component.Id;
                    string airTerminalTypeValue;
                    switch (component.LookupParameter("System Classification").AsString())
                    {
                        case ("Return Air"): airTerminalTypeValue = "outlet"; break;
                        case ("Supply Air"): airTerminalTypeValue = "inlet"; break;
                        default: airTerminalTypeValue = "bidirectional"; break;
                    }

                    // Write RDF triples:
                    sb.Append(""
                        // Air Terminal Type:
                        + $"inst:{componentID} fpo:hasAirTerminalType  \n"
                        + $"inst:{airTerminalTypeID} a fpo:AirTerminalType . \n"
                        + $"inst:{airTerminalTypeID} fpo:hasValue '{airTerminalTypeValue}'^^xsd:string . \n");
                    break;

                case ("Duct Accessories"):
                    tester.Append("\n SWITCH: duct access");
                    break;

                //+$"fso:{componentType} rdfs:subClassOf fpo:Damper . \n");
                //K_v & K_vs values:
                //sb.Append($"inst:{componentID} fpo:hasKv inst:{kvID} . \n"
                //+$"inst:{kvID} a fpo:Kv . \n"
                //+$"inst:{kvID} fpo:hasValue  '{kvValue}'^^xsd:double . \n");

                case ("Flex Ducts"):
                    tester.Append("\n SWITCH: flex");
                    break;

                default:
                    tester.Append("\n SWITCH: defautl");
                    break;
            }
        }
        public void VentilationDuctProperties(Element element)
        {
            Duct duct = (Duct)element;

            // Length
            // Diameter
            // If (has 3 connectors) KlipKanalOpOgIndsætTee(duct);
        }
        public List<Element> ManipulateDuctsWithTaps(ElementSet network)
        {
            List<Element> whateverList = new List<Element>();
            return whateverList;
        }
        public void VentilationComponentProperties(IList<Element> components)
        {
            AddBreak("VENTILATION COMPONTENT PROPERTIES - " + components.Count());

            //
            // STOPPEDE HER
            // Var igang med at få PartType ud igennem fx. MechanicalFitting
            // for at tjekke om det er en Tap, Elbow eller Tee m.v.
            //


            // Check for Tap Adjustments
            IList<Element> manipulatedComponents = components;
            tester.Append("\n Manipulate components: Før: " +  components.Count());
            foreach (Element component in components)
            {
                if (component.GetType().Name != "FamilyInstance") continue;
                else 
                {
                    //MechanicalFitting myComp = (MechanicalFitting)component;
                    string testPart = "Tee";
                    //try
                    //{
                    //    testPart = ((MechanicalFitting)component).PartType.ToString();
                    //    tester.Append(": THIS PART WORKS! " + testPart);
                    //}
                    //catch { testPart = "PART_VIRKER_IKKE"; }

                    //tester.Append("\n" + GetFamilyAndTypeName(component));


                    if (testPart == "Tee") //TapAdjustable
                    {
                        //manipulatedComponents = ManipulateDuctsWithTaps(component, manipulatedComponents);
                    }
                }
            }

            tester.Append("\n Manipulate components: Efter: " +  components.Count());




            foreach (Element component in components)
            {
                // Get RDF ID of component:
                string componentID = ID[component.UniqueId];

                tester.Append($"\n {component.GetType().Name}");
                tester.Append($" - {component.Category.Name}");

                /* BENYT
                 * component.Category.Name);     // Ducts
                 * component.GetType().Name );   // FamilyInstance
                 * 
                 * CATEGORY.NAME:       GETTYPE().NAME:
                 * -----------------------------------------
                 * Ducts                Duct
                 * Duct Placeholders    Duct
                 * Flex Ducts           FlexDuct
                 * Air Terminals        FamilyInstance
                 * Duct Accessories     FamilyInstance
                 * Duct Fittings        FamilyInstance
                 * Mechanical Equipment FamilyInstance
                 * Duct Insulations     DuctsInsulation
                 * Duct Linings         DuctsLining
                 */

                switch (component.GetType().Name)
                {
                    case ("Duct"):
                    case ("FlexDuct"):
                        //VentilationDuctProperties(component);
                        break;
                    case ("DuctsInsulation"):
                    case ("DuctsLining"):
                        //VentilationInsuLinProperties(component);
                        break;
                    case ("FamilyInstance"):
                        // AT's, fittings, access., MEQ
                        //VentilationFamilyInstanceProperties(component);
                        break;
                    
                    default : break;

                }

                // Fetch 'FSC_type' parameter from type:
                Element componentType = doc.GetElement(component.GetTypeId());
                string fscType;
                try
                {
                    fscType = componentType.LookupParameter("FSC_type").AsValueString(); // VIRKER
                } catch //(Exception ex)
                {
                    fscType = "NOTYPE";
                }

                string fsoClass = null;
                if (fscType?.Length > 0) fsoClass = fscType;
                tester.Append($" -- {fsoClass} =? {fscType}");




                // Location data
                string levelNameId = GetRdfInstFromId(component.LevelId); // OBS: Virker ikke for Insulation og Lining
                //string spaceNameId = GetRoomOfComponent(component); // !!! LHAM: Function ikke lavet endnu.
                sb.Append(""
                    + $"inst:{levelNameId} bot:hasElement inst:{componentID} . \n" // OBS: Dette skal måske genovervejes, hvis det lykkes at få bot:Space hentet ind; Det er mere retvisende at lægge elementer i spaces end på stories.
                    //+ $"inst:{spaceNameId} bot:hasElement inst:{componentNameID} . \n" // "Room has element" - skal nok kunne tage højde for liste: Blank Nodes?
                    );




                /*
                // ***** Conduct logics and extractions:
                switch (component.Category.Name)
                {
                    case ("Ducts"):
                        tester.Append("\n SWITCH: duct ");
                        fsoClass = "Segment";
                        break;

                    case ("Duct Placeholders"):
                        tester.Append("\n SWITCH: placeholder ");
                        fsoClass = "Segment";
                        break;

                    case ("Duct Fittings"):
                        tester.Append("\n SWITCH: fitting");
                        fsoClass = "Fitting";

                        FamilyInstance fitting = component as FamilyInstance;
                        MechanicalFitting fittingType = (MechanicalFitting)fitting.MEPModel;
                        PartType fittingPartType = fittingType.PartType;

                        //  sb.Append($"inst:{componentID} a fso:{fittingType} . \n");

                        //  if (fittingPartType == "Tee")

                        //  if (fittingPartType == "Elbow")
                        //      double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                        //      fpo:Angle
                        //+$"inst:{angleID} a fpo:Angle . \n"
                        //+ $"inst:{angleID} fpo:hasValue  '{angleValue}'^^xsd:double . \n"
                        //+ $"inst:{angleID} fpo:hasUnit  'Degree'^^xsd:string . \n");

                        //  if (fittingPartType == "Transition")
                        //double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("OffsetHeight").AsDouble(), UnitTypeId.Meters) // VIRKER FORKERT MED OFFSET HEIGHT!
                        //      fpo:Length  diameters/cross section areas

                        //  if (fittingPartType == "TapAdjustable") // New
                        //      // Klip duct op og indsæt Tee - ny function!

                        break;

                    case ("Mechanical Equipment"):
                        tester.Append("\n SWITCH: MEqm");
                        break;

                    case ("Air Terminals"):
                        fsoClass = "AirTerminal";
                        tester.Append("\n SWITCH: AT");
                        break;

                    case ("Duct Accessories"):
                        tester.Append("\n SWITCH: duct access");
                        break;

                        //+$"fso:{componentType} rdfs:subClassOf fpo:Damper . \n");
                        //K_v & K_vs values:
                        //sb.Append($"inst:{componentID} fpo:hasKv inst:{kvID} . \n"
                        //+$"inst:{kvID} a fpo:Kv . \n"
                        //+$"inst:{kvID} fpo:hasValue  '{kvValue}'^^xsd:double . \n");

                    case ("Flex Ducts"):
                        tester.Append("\n SWITCH: flex");
                        break;

                    default:
                        tester.Append("\n SWITCH: defautl");
                        break;
                }
                
                
                // ***** Write RDF triples:
                if(fsoClass != null) sb.Append($"{componentID} a fso:{fsoClass} . \n");
                
                // add
                // fpo:MaterialType ?
                // fpo:Length

                */


            }
        }



        //////////////////////////////////////////////////
        /***** EXCECUTION ******/

        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application; // Note: Kan ikke rykkes ud af method, fordi commandData er input!
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document document = uidoc.Document;
            doc = document;


            // Write RDF prefices:
            sb.Append(""
                + "@prefix owl: <http://www.w3.org/2002/07/owl#> . \n"
                + "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> . \n"
                + "@prefix xml: <http://www.w3.org/XML/1998/namespace> . \n"
                + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> . \n"
                + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . \n"
                + "@prefix bot: <https://w3id.org/bot#> . \n"
                + "@prefix fso: <https://w3id.org/fso#> . \n"
                + "@prefix inst: <https://example.com/inst#> . \n"
                + "@prefix rvt: <https://example.com/revit#> . \n"
                + "@prefix fpo: <https://w3id.org/fpo#> . \n"
                + "@prefix ex: <https://example.com/ex#> . \n"
                );

            //            //*************

            //BuildingExtractor();

            //LevelExtractor();

            ////SpaceExtractor();

            VentSystemExtractor();

            //VentilationComponentProperties(ventilationComponents);



            // ******************** PARSER DONE ******************** //

            // Converting to string before post request:
            string reader = sb.ToString();
            string myTester = tester.ToString();
            string myDictionary = ID.ToString();

            // Sends data to Fuseki-server:
            //var test = HttpClientHelper.POSTDataAsync(reader);


            // Pop-up dialog i Revit med Turtle-resultat
            if (myTester.Length > 0) { TaskDialog.Show("Vores debug-tester:\n", myTester); }

            //TaskDialog.Show("Parser-udtræk NY!\n", reader.Substring(520,1500));
            TaskDialog.Show("Parser-udtræk\n", reader);
            //TaskDialog.Show("Vores Dictionary:\n", myDictionary);

            // Skriver RDF-graf til txt, som test.
            string path = @"C:\Users\lham\OneDrive - Danmarks Tekniske Universitet\Revit2RDF\Parser - LHAM\MyTest.txt";
            File.WriteAllText(path, reader);




            return Result.Succeeded;










            /* DEPRECATED --- //Ventilation systems and their components. WORKING
            AddBreak(sb, "VENT SYSTEMS");
            FilteredElementCollector ventilationSystemCollector = new FilteredElementCollector(doc);
            ICollection<Element> ventilationSystems = ventilationSystemCollector.OfClass(typeof(MechanicalSystem)).ToElements();
            //List<MechanicalSystem> ventilationSystemList = new List<MechanicalSystem>();                                          // What is this used for???
            foreach (MechanicalSystem system in ventilationSystemCollector)
            {
                //Get system
                DuctSystemType systemType = system.SystemType;

                string systemTypeString = systemType.ToString();

                string systemName = system.Name; // System abbriviation - made by modeller
                string systemNameId = "VenSys_" + systemName.Replace(" ", "-");
                string systemRevitId = system.Id.ToString();
                string systemRevitGUID = system.UniqueId;
                
                ElementId superSystemType = system.GetTypeId();
                string superSystemName = doc.GetElement(superSystemType).LookupParameter("Family Name").AsValueString();
                //    string superSystemID = doc.GetElement(superSystemType).UniqueId;
                AddBreak(sb, $"Vent system {systemName}");
                switch (systemType)
                {
                    case DuctSystemType.SupplyAir:
                        sb.Append(""
                            + $"inst:{systemNameId} a fso:SupplySystem . \n"
                            );
                        break;
                    case DuctSystemType.ReturnAir:
                        sb.Append(""
                            + $"inst:{systemNameId} a fso:ReturnSystem . \n"
                            );
                        break;
                    case DuctSystemType.ExhaustAir:
                        sb.Append(""
                            + $"inst:{systemNameId} a fso:ReturnSystem . \n"    
                            );
                        break;
                    default:
                        break;
                }
                
                sb.Append(""
                    + $"inst:{systemNameId} rdfs:label \"{superSystemName}\"^^xsd:string . \n" // testet i stedet for typeName
                    + $"inst:{systemNameId} rdfs:label \"{systemName}\"^^xsd:string . \n" // testet i stedet for typeName
                    + $"inst:{systemNameId} rvt:id \"{systemRevitId}\"^^xsd:string. \n"
                    + $"inst:{systemNameId} rvt:guid \"{systemRevitGUID}\"^^xsd:string . \n"
                    );

                // ********** System Properties

                //string fluidID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //string flowTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //string fluidTemperatureID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                double fluidTemperature = 0;
                if (system.LookupParameter("Fluid Temperature") != null)
                {
                    fluidTemperature = UnitUtils.ConvertFromInternalUnits(system.LookupParameter("Fluid Temperature").AsDouble(), UnitTypeId.Celsius);
                }

                    sb.Append(""
                    $"inst:{systemName} fso:hasFlow inst:{fluidID} . \n" +
                    $"inst:{fluidID} a fso:Flow . \n" +
                    $"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} . \n" +
                    $"inst:{fluidID} fpo:hasTemperature inst:{fluidTemperatureID} . \n" +
                    $"inst:{flowTypeID} a fpo:FlowType . \n" +
                    $"inst:{flowTypeID} fpo:hasValue 'Air'^^xsd:string . \n"
                    $"inst:{fluidTemperatureID} a fpo:Temperature . \n" +
                    $"inst:{fluidTemperatureID} fpo:hasValue '{fluidTemperature}'^^xsd:double . \n" +
                    $"inst:{fluidTemperatureID} fpo:hasUnit 'Celcius'^^xsd:string . \n"


                // ********** System Components // LHAM: We only include components (incl. ducts and fittings) that are part of a system!

                ElementSet systemComponents = system.DuctNetwork;

                //Relate components to systems
                AddBreak(sb, $"Components in ventilation system {systemName}");
                foreach (Element component in systemComponents)
                {
                    //tester.Append(component.ToString() + "\n");
                    string componentID = component.Id.ToString();
                    string componentGUID = component.UniqueId;
                    string componentNameID = "Comp_" + componentID;
                    string componentLabelName = component.Parameters.ToString(); // "Ducts" "Duct Fittings" "Air Terminal" "Duct Accessories" "Duct Insulations" "Mech. Equp."
                    string componentTypeNew = "IKKE_FUNDET"; // get_parameter virkede ikke for mig...
                    //component.Category.Name; // "Ducts" "Duct Fittings" "Air Terminal" "Duct Accessories" "Duct Insulations" "Mech. Equp."
                    //component.GetType().Name; // "FamilyInstance" (= AT's, fittings, access., MEQ) "Duct" "DuctInsulation"

                      

                    tester.Append("comp-type: " + componentTypeNew + "\n");
                    tester.Append("comp-labelName: " + componentLabelName + "\n");

                    sb.Append(""
                        + $"inst:{systemNameId} fso:hasComponent inst:{componentNameID} . \n"
                        + $"inst:{componentNameID} fso:isComponentOf inst:{systemNameId} . \n"
                        + $"inst:{componentNameID} rdf:label \"{componentLabelName}\"^^xsd:string . LABEL_NAME" + "\n"
                        + $"inst:{componentNameID} rdf:label \"{componentTypeNew}\"^^xsd:string . TYPE_NEW-new" + "\n"
                        + $"inst:{componentNameID} rvt:id \"{componentID}\"^^xsd:string . \n"
                        + $"inst:{componentNameID} rvt:guid \"{componentGUID}\"^^xsd:string . \n"
                        );
                }

            }

            //************************
            // Heating and cooling systems
                        //Relationship between heating and cooling systems and their components. WORKING
                        AddBreak(sb, "HEATING AND COOLING SYSTEMS");
                        FilteredElementCollector hydraulicSystemCollector = new FilteredElementCollector(doc);
                        ICollection<Element> hydraulicSystems = hydraulicSystemCollector.OfClass(typeof(PipingSystem)).ToElements();
                        List<PipingSystem> hydraulicSystemList = new List<PipingSystem>();
                        foreach (PipingSystem system in hydraulicSystemCollector)
                        {
                            //Get systems
                            PipeSystemType systemType = system.SystemType;
                            string systemID = system.UniqueId;
                            string systemName = system.Name;
                            ElementId superSystemType = system.GetTypeId();

                            //Fluid
                            string fluidID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            string flowTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            string fluidTemperatureID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            string fluidViscosityID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            string fluidDensityID = System.Guid.NewGuid().ToString().Replace(' ', '-');

                            string flowType = doc.GetElement(superSystemType).LookupParameter("Fluid Type").AsValueString();
                            double fluidTemperature = UnitUtils.ConvertFromInternalUnits(system.LookupParameter("Fluid Temperature").AsDouble(), UnitTypeId.Celsius);
                            double fluidViscosity = UnitUtils.ConvertFromInternalUnits(doc.GetElement(superSystemType).LookupParameter("Fluid Dynamic Viscosity").AsDouble(), UnitTypeId.PascalSeconds);
                            double fluidDensity = UnitUtils.ConvertFromInternalUnits(doc.GetElement(superSystemType).LookupParameter("Fluid Density").AsDouble(), UnitTypeId.KilogramsPerCubicMeter);

                            switch (systemType)
                            {
                                case PipeSystemType.SupplyHydronic:
                                    sb.Append($"inst:{systemID} a fso:SupplySystem . \n"
                                        + $"inst:{systemID} rdfs:label '{systemName}'^^xsd:string . \n" +

                                        $"inst:{systemID} fso:hasFlow inst:{fluidID} . \n" +

                                        $"inst:{fluidID} a fso:Flow . \n" +
                                        $"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} . \n" +
                                        $"inst:{flowTypeID} a fpo:FlowType . \n" +
                                            $"inst:{flowTypeID} fpo:hasValue \"{flowType}'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasTemperature inst:{fluidTemperatureID} . \n" +
                                        $"inst:{fluidTemperatureID} a fpo:Temperature . \n" +
                                        $"inst:{fluidTemperatureID} fpo:hasValue '{fluidTemperature}'^^xsd:double . \n" +
                                        $"inst:{fluidTemperatureID} fpo:hasUnit \"Celcius'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} . \n" +
                                        $"inst:{fluidViscosityID} a fpo:Viscosity . \n" +
                                        $"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double . \n" +
                                        $"inst:{fluidViscosityID} fpo:hasUnit \"Pascal per second'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} . \n" +
                                        $"inst:{fluidDensityID} a fpo:Density . \n" +
                                        $"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double . \n" +
                                        $"inst:{fluidDensityID} fpo:hasUnit \"Kilograms per cubic meter'^^xsd:string . \n"
                                        );
                                    break;
                                case PipeSystemType.ReturnHydronic:
                                    sb.Append($"inst:{systemID} a fso:ReturnSystem . \n" +
                                        $"inst:{systemID} rdfs:label '{systemName}'^^xsd:string . \n" +

                                        $"inst:{systemID} fso:hasFlow inst:{fluidID} . \n" +

                                        $"inst:{fluidID} a fso:Flow . \n" +
                                        $"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} . \n" +
                                        $"inst:{flowTypeID} a fpo:FlowType . \n" +
                                        $"inst:{flowTypeID} fpo:hasValue \"{flowType}'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasTemperature inst:{fluidTemperatureID} . \n" +
                                        $"inst:{fluidTemperatureID} a fpo:Temperature . \n" +
                                        $"inst:{fluidTemperatureID} fpo:hasValue '{fluidTemperature}'^^xsd:double . \n" +
                                        $"inst:{fluidTemperatureID} fpo:hasUnit \"Celcius'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} . \n" +
                                        $"inst:{fluidViscosityID} a fpo:Viscosity . \n" +
                                        $"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double . \n" +
                                        $"inst:{fluidViscosityID} fpo:hasUnit \"Pascal per second'^^xsd:string . \n" +

                                        $"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} . \n" +
                                        $"inst:{fluidDensityID} a fpo:Density . \n" +
                                        $"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double . \n" +
                                        $"inst:{fluidDensityID} fpo:hasUnit \"Kilograms per cubic meter'^^xsd:string . \n"
                                        );
                                    break;
                                default:
                                    break;
                            }

                            ElementSet systemComponents = system.PipingNetwork;

                            //Relate components to systems
                            AddBreak(sb, "Components in heating/cooling system");
                            foreach (Element component in systemComponents)
                            {
                                string componentID = component.UniqueId;
                                sb.Append($"inst:{systemID} fso:hasComponent inst:{componentID} . \n");
                            }

                        }

            //************************
            // FSC types
                        StringBuilder testComponent = new StringBuilder();
                        //Get FSC_type components
                        AddBreak(sb, "FSC COMPONTENTS");
                        FilteredElementCollector componentCollector = new FilteredElementCollector(doc);
                        ICollection<Element> components = componentCollector.OfClass(typeof(FamilyInstance)).ToElements(); // LHAM: Det er måske ikke så effektivt/retvisende at hive ALLE 'FamilyInstances' ud.
                        List<FamilyInstance> componentList = new List<FamilyInstance>();
                        foreach (FamilyInstance component in componentCollector)
                        {

                            if (component.Symbol.LookupParameter("FSC_type") != null)
                            {
                                //Type
                                string componentType = component.Symbol.LookupParameter("FSC_type").AsString();
                                string componentID = component.UniqueId.ToString();
                                string revitID = component.Id.ToString();
                                sb.Append($"inst:{componentID} rvt:id inst:{revitID} . \n");

                                //Fan
                                if (componentType == "Fan")
                                {
                                    //Type 
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n");

                                    if (component.LookupParameter("FSC_pressureCurve") != null)
                                    {
                                        //PressureCurve
                                        string pressureCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string pressureCurveValue = component.LookupParameter("FSC_pressureCurve").AsString();
                                        sb.Append($"inst:{componentID} fpo:hasPressureCurve inst:{pressureCurveID} . \n"
                                         + $"inst:{pressureCurveID} a fpo:PressureCurve . \n"
                                         + $"inst:{pressureCurveID} fpo:hasCurve  '{pressureCurveValue}'^^xsd:string . \n"
                                         + $"inst:{pressureCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string . \n");
                                    }

                                    if (component.LookupParameter("FSC_powerCurve") != null)
                                    {
                                        //PowerCurve
                                        string powerCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string powerCurveValue = component.LookupParameter("FSC_powerCurve").AsString();
                                        sb.Append($"inst:{componentID} fpo:hasPowerCurve inst:{powerCurveID} . \n"
                                         + $"inst:{powerCurveID} a fpo:PowerCurve . \n"
                                         + $"inst:{powerCurveID} fpo:hasCurve  '{powerCurveValue}'^^xsd:string . \n"
                                         + $"inst:{powerCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string . \n");
                                    }

                                }
                                //Pump
                                else if (componentType == "Pump")
                                {
                                    //Type 
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n");

                                    if (component.LookupParameter("FSC_pressureCurve") != null)
                                    {
                                        //PressureCurve
                                        string pressureCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string pressureCurveValue = component.LookupParameter("FSC_pressureCurve").AsString();
                                        sb.Append($"inst:{componentID} fpo:hasPressureCurve inst:{pressureCurveID} . \n"
                                         + $"inst:{pressureCurveID} a fpo:PressureCurve . \n"
                                         + $"inst:{pressureCurveID} fpo:hasCurve  '{pressureCurveValue}'^^xsd:string . \n"
                                         + $"inst:{pressureCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string . \n");
                                    }

                                    if (component.LookupParameter("FSC_powerCurve") != null)
                                    {
                                        //PowerCurve
                                        string powerCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string powerCurveValue = component.LookupParameter("FSC_powerCurve").AsString();
                                        sb.Append($"inst:{componentID} fpo:hasPowerCurve inst:{powerCurveID} . \n"
                                         + $"inst:{powerCurveID} a fpo:PowerCurve . \n"
                                         + $"inst:{powerCurveID} fpo:hasCurve  '{powerCurveValue}'^^xsd:string . \n"
                                         + $"inst:{powerCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string . \n");
                                    }
                                }

                                //Valve
                                else if (componentType == "MotorizedValve" || componentType == "BalancingValve")
                                {
                                    //Type 
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n"
                                    + $"fso:{componentType} rdfs:subClassOf fso:Valve . \n");

                                    if (component.LookupParameter("FSC_kv") != null)
                                    {
                                        //Kv
                                        string kvID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        double kvValue = component.LookupParameter("FSC_kv").AsDouble();
                                        sb.Append($"inst:{componentID} fpo:hasKv inst:{kvID} . \n"
                                         + $"inst:{kvID} a fpo:Kv . \n"
                                         + $"inst:{kvID} fpo:hasValue  '{kvValue}'^^xsd:double . \n");
                                    }

                                    if (component.LookupParameter("FSC_kvs") != null)
                                    {
                                        //Kvs
                                        string kvsID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        double kvsValue = component.LookupParameter("FSC_kvs").AsDouble();
                                        sb.Append($"inst:{componentID} fpo:hasKvs inst:{kvsID} . \n"
                                             + $"inst:{kvsID} a fpo:Kvs . \n"
                                             + $"inst:{kvsID} fpo:hasValue  '{kvsValue}'^^xsd:double . \n");
                                    }
                                }

                                //Shunt
                                else if (componentType == "Shunt")
                                {
                                    //Type 
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n"
                                    + $"fso:{componentType} rdfs:subClassOf fpo:Valve . \n");

                                    if (component.LookupParameter("FSC_hasCheckValve") != null)
                                    {
                                        //hasCheckValve
                                        string hasCheckValveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string hasCheckValveValue = component.LookupParameter("FSC_hasCheckValve").AsValueString();
                                        sb.Append($"inst:{componentID} fpo:hasCheckValve inst:{hasCheckValveID} . \n"
                                         + $"inst:{hasCheckValveID} a fpo:CheckValve . \n"
                                         + $"inst:{hasCheckValveID} fpo:hasValue  '{hasCheckValveValue}'^^xsd:string . \n");
                                    }
                                }

                                //Damper
                                else if (componentType == "MotorizedDamper" || componentType == "BalancingDamper")
                                {
                                    //Type 
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n"
                                    + $"fso:{componentType} rdfs:subClassOf fpo:Damper . \n");

                                    if (component.LookupParameter("FSC_kv") != null)
                                    {
                                        //Kv
                                        string kvID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        double kvValue = component.LookupParameter("FSC_kv").AsDouble();
                                        sb.Append($"inst:{componentID} fpo:hasKv inst:{kvID} . \n"
                                         + $"inst:{kvID} a fpo:Kv . \n"
                                         + $"inst:{kvID} fpo:hasValue  '{kvValue}'^^xsd:double . \n");
                                    }

                                    if (component.LookupParameter("FSC_kvs") != null)
                                    {
                                        //Kvs
                                        string kvsID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        double kvsValue = component.LookupParameter("FSC_kvs").AsDouble();
                                        sb.Append($"inst:{componentID} fpo:hasKvs inst:{kvsID} . \n"
                                         + $"inst:{kvsID} a fpo:Kvs . \n"
                                         + $"inst:{kvsID} fpo:hasValue  '{kvsValue}'^^xsd:double . \n");
                                    }
                                }

                                //Pipe fittings
                                else if (component.Category.Name == "Pipe Fittings")
                                {
                                    string fittingType = ((MechanicalFitting)component.MEPModel).PartType.ToString();
                                    sb.Append($"inst:{componentID} a fso:{fittingType} . \n");

                                    if (fittingType.ToString() == "Tee")
                                    {
                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string . \n");
                                    }

                                    if (fittingType.ToString() == "Elbow")
                                    {
                                        if (component.LookupParameter("Angle") != null)
                                        {
                                            //Angle
                                            string angleID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                            double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                                            sb.Append($"inst:{componentID} fpo:hasAngle inst:{angleID} . \n"
                                             + $"inst:{angleID} a fpo:Angle . \n"
                                             + $"inst:{angleID} fpo:hasValue '{angleValue}'^^xsd:double . \n"
                                             + $"inst:{angleID} fpo:hasUnit 'Degree'^^xsd:string . \n");
                                        }

                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string . \n");
                                    }

                                    if (fittingType.ToString() == "Transition")
                                    {
                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue  '{materialTypeValue}'^^xsd:string . \n");

                                        if (component.LookupParameter("OffsetHeight") != null && component.LookupParameter("OffsetHeight").AsDouble() > 0)
                                        {
                                            //Length
                                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                            double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("OffsetHeight").AsDouble(), UnitTypeId.Meters);
                                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                                             + $"inst:{lengthID} a fpo:Length . \n"
                                             + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                                             + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                                        }
                                        else {
                                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                            double lengthValue = 0.02;
                                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                                           + $"inst:{lengthID} a fpo:Length . \n"
                                           + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                                           + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                                        }

                                    }
                                }

                                //Duct fittings
                                else if (component.Category.Name == "Duct Fittings")
                                {

                                    string fittingType = ((MechanicalFitting)component.MEPModel).PartType.ToString();
                                    sb.Append($"inst:{componentID} a fso:{fittingType} . \n");

                                    if (fittingType.ToString() == "Tee")
                                    {

                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue  '{materialTypeValue}'^^xsd:string . \n");
                                    }

                                    if (fittingType.ToString() == "Elbow")
                                    {
                                        if (component.LookupParameter("Angle") != null)
                                        {
                                            //Angle
                                            string angleID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                            double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                                            sb.Append($"inst:{componentID} fpo:hasAngle inst:{angleID} . \n"
                                             + $"inst:{angleID} a fpo:Angle . \n"
                                             + $"inst:{angleID} fpo:hasValue  '{angleValue}'^^xsd:double . \n"
                                             + $"inst:{angleID} fpo:hasUnit  'Degree'^^xsd:string . \n");
                                        }
                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue  '{materialTypeValue}'^^xsd:string . \n");

                                    }

                                    if (fittingType.ToString() == "Transition")
                                    {
                                        //MaterialType
                                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string materialTypeValue = component.Name;
                                        sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                                         + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                                         + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string . \n");

                                        if (component.LookupParameter("OffsetHeight") != null && component.LookupParameter("OffsetHeight").AsDouble() > 0)
                                        {
                                            //Length
                                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                            double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("OffsetHeight").AsDouble(), UnitTypeId.Meters);
                                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                                             + $"inst:{lengthID} a fpo:Length . \n"
                                             + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                                             + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                                        }
                                        else
                                        {
                                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                            double lengthValue = 0.02;
                                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                                           + $"inst:{lengthID} a fpo:Length . \n"
                                           + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                                           + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                                        }
                                    }

                                    // **** LHAM:HER kunne man indføre handling på "TapAdjustable"
                                }


                                //Radiator
                                else if (componentType == "Radiator")
                                {
                                    //Type
                                    sb.Append($"inst:{componentID} a fso:SpaceHeater . \n");

                                    //DesignHeatPower
                                    string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                    double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("FSC_nomPower").AsDouble(), UnitTypeId.Watts);
                                    sb.Append($"inst:{componentID} fpo:hasDesignHeatingPower inst:{designHeatPowerID} . \n"
                                     + $"inst:{designHeatPowerID} a fpo:DesignHeatingPower . \n"
                                     + $"inst:{designHeatPowerID} fpo:hasValue  '{designHeatPowerValue}'^^xsd:double . \n"
                                     + $"inst:{designHeatPowerID} fpo:hasUnit  'Watts'^^xsd:string . \n");

                                        if ( component.Space != null)
                                        {
                                            //string s = component.Space.Name;
                                            string relatedRoomID = component.Space.UniqueId.ToString();
                                            sb.Append($"inst:{componentID} fso:transfersHeatTo inst:{relatedRoomID} . \n");
                                        }


                                }

                                //AirTerminal
                                else if (componentType == "AirTerminal")
                                {
                                    //Type
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n");

                                    if (component.LookupParameter("System Classification").AsString() == "Return Air")
                                    {
                                        //AirTerminalType
                                        string airTerminalTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string airTerminalTypeValue = "outlet";
                                        sb.Append($"inst:{componentID} fpo:hasAirTerminalType inst:{airTerminalTypeID} . \n"
                                         + $"inst:{airTerminalTypeID} a fpo:AirTerminalType . \n"
                                         + $"inst:{airTerminalTypeID} fpo:hasValue '{airTerminalTypeValue}'^^xsd:string . \n");

            //                            //Relation to room and space
            //                            string relatedRoomID = component.Space.UniqueId.ToString();
            //                            sb.Append($"inst:{relatedRoomID} fso:suppliesFluidTo inst:{componentID} . \n");

                                        //Adding a fictive port the airterminal which is not included in Revit
                                        string connectorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        sb.Append($"inst:{componentID} fso:hasPort inst:{connectorID} . \n"
                                            + $"inst:{connectorID} a fso:Port . \n");

                                        //Diameter to fictive port 

                                        //FlowDirection to fictive port
                                        string connectorDirectionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string connectorDirection = "In";

                                        sb.Append($"inst:{connectorID} fpo:hasFlowDirection inst:{connectorDirectionID} . \n"
                                                                + $"inst:{connectorDirectionID} a fpo:FlowDirection . \n"
                                                                + $"inst:{connectorDirectionID} fpo:hasValue '{connectorDirection}'^^xsd:string . \n");
                                    }


                                    if (component.LookupParameter("System Classification").AsString() == "Supply Air")
                                    {
                                        //AirTerminalType
                                        string airTerminalTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        string airTerminalTypeValue = "inlet";
                                        sb.Append($"inst:{componentID} fpo:hasAirTerminalType inst:{airTerminalTypeID} . \n"
                                         + $"inst:{airTerminalTypeID} a fpo:AirTerminalType . \n"
                                         + $"inst:{airTerminalTypeID} fpo:hasValue '{airTerminalTypeValue}'^^xsd:string . \n");

            //                            string relatedRoomID = component.Space.UniqueId.ToString();
            //                            sb.Append($"inst:{componentID} fso:suppliesFluidTo inst:{relatedRoomID} . \n");

                                        //Adding a fictive port the airterminal which is not included in Revit
                                        string connectorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        sb.Append($"inst:{componentID} fso:hasPort inst:{connectorID} . \n"
                                            + $"inst:{connectorID} a fso:Port . \n");

                                        //FlowDirection
                                        string connectorDirectionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        string connectorDirection = "Out";

                                        sb.Append($"inst:{connectorID} fpo:hasFlowDirection inst:{connectorDirectionID} . \n"
                                                                + $"inst:{connectorDirectionID} a fpo:FlowDirection . \n"
                                                                + $"inst:{connectorDirectionID} fpo:hasValue '{connectorDirection}'^^xsd:string . \n");


                                        //Fictive pressureDrop
                                        string pressureDropID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                                        double pressureDropValue = 5;
                                        sb.Append($"inst:{connectorID} fpo:hasPressureDrop inst:{pressureDropID} . \n"
                                       + $"inst:{pressureDropID} a fpo:PressureDrop . \n"
                                       + $"inst:{pressureDropID} fpo:hasValue '{pressureDropValue}'^^xsd:double . \n"
                                       + $"inst:{pressureDropID} fpo:hasUnit 'Pascal'^^xsd:string . \n");

                                        //if (component.LookupParameter("Flow") != null)
                                        //{
                                        //    //Flow rate
                                        //    string flowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        //    double flowValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Flow").AsDouble(), UnitTypeId.LitersPerSecond);
                                        //    sb.Append($"inst:{connectorID} fpo:flowRate inst:{flowID} . \n"
                                        //     + $"inst:{flowID} a fpo:FlowRate . \n"
                                        //     + $"inst:{flowID} fpo:hasValue '{flowValue}'^^xsd:double . \n"
                                        //     + $"inst:{flowID} fpo:hasUnit 'Liters per second'^^xsd:string . \n");
                                        //}


                                    }

                                }

                                else
                                {
                                    // LHAM: Debug her:
                                    // Find ud af, hvordan man kan skelne mellem Duct Acc. og MechEqm.
                                    if (component.MEPModel.ConnectorManager != null)
                                    {
                                        testComponent.Append($"\nConnecter != null:_____\nRevitID: {revitID}\nComponent: {component}\nComponent Type: {componentType}"); // LHAM: (fjernet .toString() på comp.)

                                        if (componentType != "HeatExchanger") //componentType != "") // LHAM: Har tilføjet != "" for at frasortere Mechanical Equipment uden connectors.
                                        {
                                            testComponent.Append($"\nAlso: ComponentType != HEX\n");
                                            //TaskDialog.Show("So far, so good", $"Vores component er et: \ncomp: {component}\nRvt ID: {revitID}\nType:{componentType} <--"); // LHAM: PAS PÅ med denne. Den kommer ved ALLE componenter!
                                            //if(component.MEPModel.ConnectorManager != null)
                                            //{
                                                RelatedPorts.FamilyInstanceConnectors(component, revitID, componentID, sb);
                                            //}
                                        }
                                    }
                                }
                            }
                        }

                        // DEBUG ! - Finder alle componenter, der bliver fanget i sidste 'if' clause.
                        string testPath = @"C:\Users\lham\OneDrive - Danmarks Tekniske Universitet\Revit2RDF\Parser-main\MyDifficultComponents.txt";
                        File.WriteAllText(testPath, testComponent.ToString());

            //************************
            // FSC Heat Exchanger
                        ////Get FSC HeatExchanger_type components
                        AddBreak(sb, "FSC HEAT-EXCHANGER");
                        FilteredElementCollector heatExchangerCollector = new FilteredElementCollector(doc);
                        ICollection<Element> heatExchangers = heatExchangerCollector.OfClass(typeof(FamilyInstance)).ToElements();
                        List<FamilyInstance> heatExchangerList = new List<FamilyInstance>();
                        foreach (FamilyInstance component in heatExchangerCollector)
                        {
                            if (component.Symbol.LookupParameter("FSC_type") != null)
                            {
                                ////HeatExchanger
                                if (component.Symbol.LookupParameter("FSC_type").AsString() == "HeatExchanger")
                                {
                                    //Type
                                    string componentType = component.Symbol.LookupParameter("FSC_type").AsString();
                                    string componentID = component.UniqueId.ToString();
                                    string revitID = component.Id.ToString();
                                    sb.Append($"inst:{componentID} rvt:id inst:{revitID} . \n");
                                    sb.Append($"inst:{componentID} a fso:{componentType} . \n");

                                    if (component.LookupParameter("FSC_nomPower") != null)
                                    {
                                        //DesignHeatPower
                                        string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                                        double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("FSC_nomPower").AsDouble(), UnitTypeId.Watts);
                                        sb.Append($"inst:{componentID} fpo:hasDesignHeatingPower inst:{designHeatPowerID} . \n"
                                         + $"inst:{designHeatPowerID} a fpo:DesignHeatingPower . \n"
                                         + $"inst:{designHeatPowerID} fpo:hasValue  '{designHeatPowerValue}'^^xsd:double . \n"
                                         + $"inst:{designHeatPowerID} fpo:hasUnit  'Watts'^^xsd:string . \n");
                                    }

                                    //RelatedPorts.HeatExchangerConnectors(component, componentID, sb);

                                }
                            }
                        }

            //************************

            //PIPES
            //Get all pipes
            AddBreak(sb, "PIPES");
            FilteredElementCollector pipeCollector = new FilteredElementCollector(doc);
            ICollection<Element> pipes = pipeCollector.OfClass(typeof(Pipe)).ToElements();
            List<Pipe> pipeList = new List<Pipe>();
            foreach (Pipe component in pipeCollector)
            {
                Pipe w = component as Pipe;

                //Type
                string componentID = component.UniqueId.ToString();
                string revitID = component.Id.ToString();
                sb.Append(
                    $"inst:{componentID} a fso:Pipe . \n" + 
                    $"inst:{componentID} rvt:id inst:{revitID} . \n" );

                if (component.PipeType.Roughness != null)
                {
                    //Roughness
                    string roughnessID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double rougnessValue = component.PipeType.Roughness;
                    sb.Append($"inst:{componentID} fpo:hasRoughness inst:{roughnessID} . \n"
                     + $"inst:{roughnessID} a fpo:Roughness . \n"
                     + $"inst:{roughnessID} fpo:hasValue '{rougnessValue}'^^xsd:double . \n" +
                     $"inst:{roughnessID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                }
                if (component.LookupParameter("Length") != null)
                {
                    //Length
                    string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Length").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                     + $"inst:{lengthID} a fpo:Length . \n"
                     + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                     + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                }

                //MaterialType
                string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                string materialTypeValue = component.Name;
                sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                 + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                 + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string . \n");


                //RelatedPorts.PipeConnectors(component, componentID, sb);

            }
            // End pipes
            //************************

            //Get all ducts 
            AddBreak(sb, "DUCTS");
            FilteredElementCollector ductCollector = new FilteredElementCollector(doc);
            ICollection<Element> ducts = ductCollector.OfClass(typeof(Duct)).ToElements();
            List<Duct> ductList = new List<Duct>();
            foreach (Duct component in ductCollector)
            {
                Duct w = component as Duct;

                //Type
                string componentID = component.UniqueId.ToString();
                string revitID = component.Id.ToString();
                string level = component.ReferenceLevel.Name;
               
                sb.Append(""
                    + $"inst:{revitID} a fso:Duct . \n"
                    + $"inst:{revitID} rvt:id '{revitID}'^^xsd:string . \n"
                    + $"inst:{revitID} rvt:guid '{componentID}'^^xsd:string . \n"
                    + $"bot:{level} bot:hasElement inst:{revitID} . \n" // OBS: Dette skal måske genovervejes, hvis det lykkes at få bot:Space hentet ind. Det er mere retvisende at lægge elementer i spaces end på stories.
                    );

                if (component.DuctType.Roughness != null)
                {
                    //Roughness
                    string roughnessID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double rougnessValue = component.DuctType.Roughness;
                    sb.Append($"inst:{componentID} fpo:hasRoughness inst:{roughnessID} . \n"
                     + $"inst:{roughnessID} a fpo:Roughness . \n"
                     + $"inst:{roughnessID} fpo:hasValue '{rougnessValue}'^^xsd:double . \n" +
                     $"inst:{roughnessID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                }

                if (component.LookupParameter("Length") != null)
                {
                    //Length
                    string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                    double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Length").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} . \n"
                     + $"inst:{lengthID} a fpo:Length . \n"
                     + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double . \n"
                     + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                }

                if (component.LookupParameter("Hydraulic Diameter") != null)
                {
                    //Outside diameter
                    string outsideDiameterID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double outsideDiameterValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Hydraulic Diameter").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasHydraulicDiameter inst:{outsideDiameterID} . \n"
                     + $"inst:{outsideDiameterID} a fpo:HydraulicDiameter . \n"
                     + $"inst:{outsideDiameterID} fpo:hasValue '{outsideDiameterValue}'^^xsd:double . \n"
                     + $"inst:{outsideDiameterID} fpo:hasUnit 'Meter'^^xsd:string . \n");
                }
              
                    //MaterialType
                    string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    string materialTypeValue = component.Name;
                    sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} . \n"
                     + $"inst:{materialTypeID} a fpo:MaterialType . \n"
                     + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string . \n");
           

                if (component.LookupParameter("Loss Coefficient") != null)
                {
                    //frictionFactor 
                    string frictionFactorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double frictionFactorValue = component.LookupParameter("Loss Coefficient").AsDouble();
                    sb.Append($"inst:{componentID} fpo:hasFrictionFactor inst:{frictionFactorID} . \n"
                     + $"inst:{frictionFactorID} a fpo:FrictionFactor . \n"
                     + $"inst:{frictionFactorID} fpo:hasValue '{frictionFactorValue}'^^xsd:double . \n");
                }

                if (component.LookupParameter("Friction") != null)
                {
                    //friction
                    string frictionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double frictionIDValue = component.LookupParameter("Friction").AsDouble();
                    sb.Append(""
                        + $"inst:{componentID} fpo:hasFriction inst:{frictionID} . \n"
                        + $"inst:{frictionID} a fpo:Friction . \n"
                        + $"inst:{frictionID} fpo:hasValue '{frictionIDValue}'^^xsd:double . \n"
                        + $"inst:{frictionID} fpo:hasUnit 'Pascal per meter'^^xsd:string . \n"
                     );
                }
            
                //RelatedPorts.DuctConnectors(component, componentID, sb);
            }

            */



        }
        

    }



}
