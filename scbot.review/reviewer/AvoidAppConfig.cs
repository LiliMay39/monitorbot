﻿using scbot.review.diffparser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace scbot.review.reviewer
{
    class AvoidAppConfig : DiffReviewerBase
    {
        public override void Visit(AddedLine line)
        {
            base.Visit(line);
            if (CurrentNewFile.EndsWith("app.config", StringComparison.InvariantCultureIgnoreCase))
            {
                Comments.Add(new DiffComment("App.config edited", CurrentNewFile, CurrentNewFileLineNumber));
            }
        }
    }
}
