﻿using System.ComponentModel;
using System.Linq;
using Raven.Database.Linq;

namespace Raven.Tests.Indexes
{
    [DisplayName("Compiled/View")]
    public class MapOnlyView : AbstractViewGenerator
    {
        public MapOnlyView()
        {
            AddField("CustomerId");
            MapDefinition = source => from doc in source
                                      select doc;
        }
    }
}