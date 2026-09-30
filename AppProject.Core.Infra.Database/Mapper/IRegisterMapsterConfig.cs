using System;
using Mapster;

namespace AppProject.Core.Infra.Database.Mapper;

public interface IRegisterMapsterConfig
{
    void Register(TypeAdapterConfig config);
}
