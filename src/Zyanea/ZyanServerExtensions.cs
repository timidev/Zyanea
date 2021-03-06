﻿using System;
using System.Collections.Generic;
using System.Text;
using DryIoc;

namespace Zyanea
{
    public static class ZyanServerExtensions
	{
		public static void Register<IService, Service>(this ZyanServer self)
			where Service : IService
		{
			self.Container.Register<IService, Service>();
		}

		public static void Register<IService, Service>(this ZyanServer self, IReuse reuse)
			where Service : IService
		{
			self.Container.Register<IService, Service>(reuse);
		}

		public static IService Resolve<IService>(this ZyanServer self)
		{
			return self.Container.Resolve<IService>();
		}
	}
}
