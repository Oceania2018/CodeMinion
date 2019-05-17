﻿using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CodeMinion.Core.Models
{
    public class Declaration
    {
        public string Name { get; set; }
        public List<Argument> Arguments { get; set; }
        public List<Argument> returns { get; set; }
        public bool IsDeprecated { get; set; }

        public Declaration Clone()
        {
            return JObject.FromObject(this).ToObject<Declaration>();
        }
    }
}
