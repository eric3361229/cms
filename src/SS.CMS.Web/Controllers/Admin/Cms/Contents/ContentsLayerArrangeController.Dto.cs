﻿using SS.CMS.Abstractions.Dto.Request;

namespace SS.CMS.Web.Controllers.Admin.Cms.Contents
{
    public partial class ContentsLayerArrangeController
    {
        public class SubmitRequest : ChannelRequest
        {
            public string AttributeName { get; set; }
            public bool IsDesc { get; set; }
        }
    }
}