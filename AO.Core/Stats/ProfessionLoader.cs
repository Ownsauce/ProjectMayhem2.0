using System.Xml.Linq;
using System.Collections.Generic;
using System.IO;

namespace AO.Core.Stats
{
    public static class ProfessionLoader
    {
        public static List<Profession> LoadFromXml(string path)
        {
            var xmlText = File.ReadAllText(path);

            // Wrap with a root element to make valid XML
            var wrappedXml = $"<root>{xmlText}</root>";

            var doc = XDocument.Parse(wrappedXml);

            var professions = new List<Profession>();

            foreach (var profElem in doc.Descendants("profession"))
            {
                var profession = new Profession
                {
                    Name = profElem.Attribute("name")?.Value
                };

                foreach (var statElem in profElem.Descendants("stat"))
                {
                    var stat = new StatDefinition
                    {
                        Name = statElem.Attribute("name")?.Value
                    };

                    foreach (var levelElem in statElem.Descendants("levelrange"))
                    {
                        stat.LevelRanges.Add(new LevelRange
                        {
                            Min = int.Parse(levelElem.Attribute("min").Value),
                            Max = int.Parse(levelElem.Attribute("max").Value),
                            Priority = int.Parse(levelElem.Attribute("pri").Value)
                        });
                    }

                    profession.Stats.Add(stat);
                }

                professions.Add(profession);
            }

            return professions;
        }
    }
}
