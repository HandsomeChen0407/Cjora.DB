namespace Cjora.DB.Services;

public static class SqlSugarSetup
{
    /// <summary>
    /// 敏感字段缓存
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SensitivePropsCache = new();

    /// <summary>
    /// SqlSugar 上下文初始化
    /// </summary>
    /// <param name="services"></param>
    public static void AddSqlSugar(this IServiceCollection services)
    {
        // 注册雪花Id
        var snowIdOpt = App.GetConfig<SnowIdOptions>("SnowId", true);
        YitIdHelper.SetIdGenerator(snowIdOpt);

        // 自定义 SqlSugar 雪花ID算法
        SnowFlakeSingle.WorkId = snowIdOpt.WorkerId;
        StaticConfig.CustomSnowFlakeFunc = () =>
        {
            return YitIdHelper.NextId();
        };

        var dbOptions = App.GetConfig<DbConnectionOptions>("DbConnection", true);
        dbOptions.ConnectionConfigs.ForEach(SetDbConfig);

        SqlSugarScope sqlSugar = new(dbOptions.ConnectionConfigs.Adapt<List<ConnectionConfig>>(), db =>
        {
            dbOptions.ConnectionConfigs.ForEach(config =>
            {
                var dbProvider = db.GetConnectionScope(config.ConfigId);
                SetDbAop(dbProvider, dbOptions.EnableSqlAopLog);
            });
        });

        services.AddSingleton<ISqlSugarClient>(sqlSugar); // 单例注册
        services.AddScoped(typeof(SqlSugarRepository<>)); // 仓储注册
        services.AddUnitOfWork<SqlSugarUnitOfWork>(); // 事务与工作单元注册
    }

    /// <summary>
    /// 配置连接属性
    /// </summary>
    /// <param name="config"></param>
    public static void SetDbConfig(DbConnectionConfig config)
    {
        config.InitKeyType = InitKeyType.Attribute;
        config.IsAutoCloseConnection = true;
        config.MoreSettings = new ConnMoreSettings
        {
            IsAutoRemoveDataCache = true,
            IsAutoDeleteQueryFilter = true, // 启用删除查询过滤器
            IsAutoUpdateQueryFilter = true, // 启用更新查询过滤器
            SqlServerCodeFirstNvarchar = true // 采用Nvarchar
        };
    }

    /// <summary>
    /// 配置Aop
    /// </summary>
    /// <param name="db"></param>
    /// <param name="enableSqlAopLog"></param>
    public static void SetDbAop(SqlSugarScopeProvider db, bool enableSqlAopLog)
    {
        var config = db.CurrentConnectionConfig;

        // 设置超时时间
        db.Ado.CommandTimeOut = 30;

        // 打印SQL语句
        if (enableSqlAopLog)
        {
            // 测试用例，正式环境建议注释掉
            //db.Aop.OnLogExecuting = (sql, pars) =>
            //{
            //    var originColor = Console.ForegroundColor;
            //    if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            //        Console.ForegroundColor = ConsoleColor.Green;
            //    if (sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            //        Console.ForegroundColor = ConsoleColor.Yellow;
            //    if (sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            //        Console.ForegroundColor = ConsoleColor.Red;
            //    Console.WriteLine("【" + DateTime.Now + "——执行SQL】\r\n" + UtilMethods.GetSqlString(config.DbType, sql, pars) + "\r\n");
            //    Console.ForegroundColor = originColor;
            //};
            db.Aop.OnError = ex =>
            {
                if (ex.Parametres == null) return;
                var pars = db.Utilities.SerializeObject(((SugarParameter[])ex.Parametres).ToDictionary(it => it.ParameterName, it => it.Value));
                Furion.Logging.Log.Error($"{ex.Message}{Environment.NewLine}{ex.Sql}{pars}{Environment.NewLine}");
            };
            db.Aop.OnLogExecuted = (sql, pars) =>
            {
                // 执行时间超过5秒
                if (db.Ado.SqlExecutionTime.TotalSeconds > 5)
                {
                    var fileName = db.Ado.SqlStackTrace.FirstFileName; // 文件名
                    var fileLine = db.Ado.SqlStackTrace.FirstLine; // 行号
                    var firstMethodName = db.Ado.SqlStackTrace.FirstMethodName; // 方法名
                    var log = $"【所在文件名】：{fileName}\r\n【代码行数】：{fileLine}\r\n【方法名】：{firstMethodName}\r\n" + $"【sql语句】：{UtilMethods.GetSqlString(config.DbType, sql, pars)}";
                    Furion.Logging.Log.Warning(log);
                }
            };
        }
        // 数据审计
        db.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            if (entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                // 主键(long类型)且没有值的---赋值雪花Id
                if (entityInfo.EntityColumnInfo.IsPrimarykey && entityInfo.EntityColumnInfo.PropertyInfo.PropertyType == typeof(long))
                {
                    var id = entityInfo.EntityColumnInfo.PropertyInfo.GetValue(entityInfo.EntityValue);
                    if (id == null || (long)id == 0)
                        entityInfo.SetValue(YitIdHelper.NextId());
                }
                // 若创建时间为空则赋值当前时间
                else if (entityInfo.PropertyName == nameof(EntityBase.CreateTime))
                {
                    var createTime = entityInfo.EntityColumnInfo.PropertyInfo.GetValue(entityInfo.EntityValue)!;
                    if (createTime == null || createTime.Equals(DateTime.MinValue))
                        entityInfo.SetValue(DateTime.Now);
                }
                if (App.User != null)
                {
                    if (entityInfo.PropertyName == nameof(EntityTenantId.TenantId))
                    {
                        var tenantId = ((dynamic)entityInfo.EntityValue).TenantId;
                        if (tenantId == null || tenantId == 0)
                            entityInfo.SetValue(App.User.FindFirst(ClaimConst.TenantId)?.Value);
                    }
                    else if (entityInfo.PropertyName == nameof(EntityBase.CreateUserId))
                    {
                        var createUserId = ((dynamic)entityInfo.EntityValue).CreateUserId;
                        if (createUserId == 0 || createUserId == null)
                            entityInfo.SetValue(App.User.FindFirst(ClaimConst.UserId)?.Value);
                    }
                    else if (entityInfo.PropertyName == nameof(EntityBase.CreateUserName))
                    {
                        var createUserName = ((dynamic)entityInfo.EntityValue).CreateUserName;
                        if (string.IsNullOrEmpty(createUserName))
                            entityInfo.SetValue(FakeEncrypt(App.User.FindFirst(ClaimConst.RealName)?.Value));
                    }
                    else if (entityInfo.PropertyName == nameof(EntityBaseData.CreateOrgId))
                    {
                        var createOrgId = ((dynamic)entityInfo.EntityValue).CreateOrgId;
                        if (createOrgId == 0 || createOrgId == null)
                            entityInfo.SetValue(App.User.FindFirst(ClaimConst.OrgId)?.Value);
                    }
                    else if (entityInfo.PropertyName == nameof(EntityBaseData.CreateOrgName))
                    {
                        var createOrgName = ((dynamic)entityInfo.EntityValue).CreateOrgName;
                        if (string.IsNullOrEmpty(createOrgName))
                            entityInfo.SetValue(App.User.FindFirst(ClaimConst.OrgName)?.Value);
                    }
                }
            }
            if (entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                if (entityInfo.PropertyName == nameof(EntityBase.UpdateTime))
                    entityInfo.SetValue(DateTime.Now);
                else if (entityInfo.PropertyName == nameof(EntityBase.UpdateUserId))
                {
                    var updateUserId = ((dynamic)entityInfo.EntityValue).UpdateUserId;
                    if (updateUserId == 0 || updateUserId == null)
                        entityInfo.SetValue(App.User?.FindFirst(ClaimConst.UserId)?.Value);
                }
                else if (entityInfo.PropertyName == nameof(EntityBase.UpdateUserName))
                {
                    var updateUserName = ((dynamic)entityInfo.EntityValue).UpdateUserName;
                    if (string.IsNullOrEmpty(updateUserName))
                        entityInfo.SetValue(FakeEncrypt(App.User?.FindFirst(ClaimConst.RealName)?.Value));
                }

            }

            // 新增/修改
            if (entityInfo.OperationType == DataFilterType.InsertByObject || entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                // 检查字段是否带有 SensitiveColumnAttribute
                var prop = entityInfo.EntityColumnInfo?.PropertyInfo;
                if (prop != null && Attribute.IsDefined(prop, typeof(SensitiveColumnAttribute), true))
                {
                    var rawVal = oldValue?.ToString();
                    if (!string.IsNullOrEmpty(rawVal))
                    {
                        // 执行加密逻辑
                        var encrypted = FakeEncrypt(rawVal);
                        entityInfo.SetValue(encrypted);
                    }
                }
            }
        };

        db.Aop.OnExecutingChangeSql = (sql, pars) =>
        {
            if (string.IsNullOrWhiteSpace(sql) || pars == null || pars.Length == 0)
                return new KeyValuePair<string, SugarParameter[]>(sql, pars);

            // 非查询 SQL 不处理
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return new KeyValuePair<string, SugarParameter[]>(sql, pars);

            var newPars = pars.ToList();
            var addedParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool sqlChanged = false;

            // 找出涉及的敏感表
            var involvedTables = SensitiveColumnCache.AllCaches
                .Where(kv => sql.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (!involvedTables.Any())
                return new KeyValuePair<string, SugarParameter[]>(sql, pars);

            foreach (var kv in involvedTables)
            {
                var sensitiveCols = kv.Value;

                foreach (var (prop, colName) in sensitiveCols)
                {
                    // 捕获 [可选别名].[列名] = @param / :param
                    string pattern = $@"((?:[""]?\w+[""]?)\.)?(?<!\w)[""]?{Regex.Escape(colName)}[""]?(?!\w)\s*=\s*([@:]\w+)";
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                    foreach (Match match in regex.Matches(sql))
                    {
                        string alias = match.Groups[1].Value; // 带点，例如 "a." 或 "b."
                        string paramInSql = match.Groups[2].Value;

                        var par = pars.FirstOrDefault(p =>
                            string.Equals(p.ParameterName, paramInSql, StringComparison.OrdinalIgnoreCase));
                        if (par == null || par.Value == null) continue;

                        string paramVal = par.Value.ToString()!;
                        if (string.IsNullOrEmpty(paramVal)) continue;

                        bool isEncrypted = paramVal.StartsWith("ENC:");
                        string encVal = isEncrypted ? paramVal : FakeEncrypt(paramVal);
                        string rawVal = isEncrypted ? FakeDecrypt(paramVal) : paramVal;

                        string encParamName = par.ParameterName + "_enc";
                        if (!addedParamNames.Contains(encParamName))
                        {
                            newPars.Add(new SugarParameter(encParamName, encVal));
                            addedParamNames.Add(encParamName);
                        }

                        string rawParamName = par.ParameterName + "_raw";
                        if (!addedParamNames.Contains(rawParamName))
                        {
                            newPars.Add(new SugarParameter(rawParamName, rawVal));
                            addedParamNames.Add(rawParamName);
                        }

                        // 保留别名，避免字段歧义
                        string replacement = $"({alias}{colName} = {rawParamName} OR {alias}{colName} = {encParamName})";

                        if (!sql.Contains(replacement, StringComparison.OrdinalIgnoreCase))
                        {
                            sql = regex.Replace(sql, replacement, 1);
                            sqlChanged = true;
                        }
                    }
                }
            }

            return sqlChanged
                ? new KeyValuePair<string, SugarParameter[]>(sql, newPars.ToArray())
                : new KeyValuePair<string, SugarParameter[]>(sql, pars);
        };

        // 数据执行后处理敏感字段
        db.Aop.DataExecuted = (value, entity) =>
        {
            if (value == null) return;

            // 如果是集合且不是字符串
            if (value is IEnumerable<object> list && !(value is string))
                HandleSensitivePropertiesParallel(list);
            else
                HandleSensitivePropertiesParallel(new[] { value });
        };

        // 配置假删除过滤器
        db.QueryFilter.AddTableFilter<IDeletedFilter>(u => u.IsDelete == false);

        // 超管排除其他过滤器
        if (App.User?.FindFirst(ClaimConst.AccountType)?.Value == SqlSugarConst.SuperAdmin)
            return;

        // 配置租户过滤器
        var tenantId = App.User?.FindFirst(ClaimConst.TenantId)?.Value;
        if (!string.IsNullOrWhiteSpace(tenantId))
            db.QueryFilter.AddTableFilter<ITenantIdFilter>(u => u.TenantId == long.Parse(tenantId));

        // 配置用户机构（数据范围）过滤器
        SqlSugarFilter.SetOrgEntityFilter(db);

        // 配置自定义过滤器
        SqlSugarFilter.SetCustomEntityFilter(db);
    }

    /// <summary>
    /// 数据脱敏辅助方法
    /// </summary>
    /// <param name="entity"></param>
    private static void HandleSensitiveProperties(object entity)
    {
        if (entity == null) return;

        var type = entity.GetType();
        var properties = type.GetProperties()
            .Where(p => p.IsDefined(typeof(SensitiveColumnAttribute), true));

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite) continue;

            var oldVal = prop.GetValue(entity)?.ToString();
            if (string.IsNullOrEmpty(oldVal)) continue;

            // 获取特性
            var attr = prop.GetCustomAttributes(typeof(SensitiveColumnAttribute), true)
                           .Cast<SensitiveColumnAttribute>()
                           .FirstOrDefault();
            if (attr == null) continue;

            // 解密
            string decrypted = FakeDecrypt(oldVal);

            //// 如果用户有“脱敏”权限
            //bool skipMask = CheckUserHasPermission();
            //if (skipMask)
            //{
            //    decrypted = MaskByType(decrypted, attr);
            //}

            prop.SetValue(entity, decrypted);
        }
    }

    /// <summary>
    /// 加密
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string FakeEncrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("ENC:")) return value;
        return $"ENC:{SensitiveColumnAESHelper.Encrypt(value)}";
    }

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string FakeDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("ENC:")) return SensitiveColumnAESHelper.Decrypt(value.Substring(4));
        return value;
    }

    /// <summary>
    /// 并行处理敏感字段
    /// </summary>
    /// <param name="entities"></param>
    public static void HandleSensitivePropertiesParallel(IEnumerable<object> entities)
    {
        if (entities == null) return;

        Parallel.ForEach(entities, entity =>
        {
            if (entity == null) return;

            var type = entity.GetType();

            // 获取敏感字段缓存
            var properties = SensitivePropsCache.GetOrAdd(type, t =>
                t.GetProperties()
                 .Where(p => p.CanRead && p.CanWrite && p.IsDefined(typeof(SensitiveColumnAttribute), true))
                 .ToArray()
            );

            if (properties.Length == 0) return;

            foreach (var prop in properties)
            {
                var oldVal = prop.GetValue(entity)?.ToString();
                if (string.IsNullOrEmpty(oldVal)) continue;

                // 解密
                string decrypted = FakeDecrypt(oldVal);

                prop.SetValue(entity, decrypted);
            }
        });
    }
}