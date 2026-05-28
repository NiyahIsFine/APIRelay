using System.Globalization;

namespace APIRelay
{
    internal enum TextId
    {
        Txt1, // 配置供应商
        Txt2, // 配置模型价格
        Txt3, // 清空当前日期统计
        Txt4, // 清空全部统计
        Txt5, // 语言
        Txt6, // 代理配置
        Txt7, // 本地监听地址
        Txt8, // 调用地址
        Txt9, // 发往服务器
        Txt10, // 发给工具
        Txt11, // 复制
        Txt12, // 启动时自动启动代理
        Txt13, // 打开日志
        Txt14, // 启动代理
        Txt15, // 停止代理
        Txt16, // 保存配置
        Txt17, // 状态
        Txt18, // Token 使用统计
        Txt19, // 输入
        Txt20, // 输出
        Txt21, // 缓存命中
        Txt22, // 花费/成本
        Txt23, // 当前日期半小时趋势
        Txt24, // 记录日期
        Txt25, // 时间
        Txt26, // 模型
        Txt27, // 路径
        Txt28, // 输入/缓存读取
        Txt29, // 用时/首字
        Txt30, // 统计：全部日期
        Txt31, // 统计：当前日期
        Txt32, // 运行中
        Txt33, // 已停止
        Txt34, // 空/不转换
        Txt35, // 设置已保存
        Txt36, // 调用地址已复制
        Txt37, // 复制调用地址失败
        Txt38, // 复制失败
        Txt39, // 清空指定日期确认
        Txt40, // 确认清空当前日期
        Txt41, // 指定日期记录已清空
        Txt42, // 清空全部日期确认
        Txt43, // 确认清空全部统计
        Txt44, // 删除记录失败
        Txt45, // 全部日期记录已清空
        Txt46, // 打开日志失败
        Txt47, // 打开失败
        Txt48, // 模型价格配置已保存
        Txt49, // 运行中不能修改供应商配置
        Txt50, // 运行中不可修改
        Txt51, // 供应商配置已保存
        Txt52, // 隐藏悬浮统计
        Txt53, // 打开主窗口
        Txt54, // 退出
        Txt55, // APIRelay 正在运行
        Txt56, // 显示悬浮统计
        Txt57, // 本地监听地址无效说明
        Txt58, // 地址无效
        Txt59, // 已启动监听
        Txt60, // 供应商路由说明
        Txt61, // 内部日志
        Txt62, // 启动监听失败
        Txt63, // 启动失败
        Txt64, // 代理服务异常
        Txt65, // 运行异常
        Txt66, // 请求被拒绝
        Txt67, // 请求
        Txt68, // 回复完成
        Txt69, // 请求已取消
        Txt70, // APIRelay 转发失败
        Txt71, // 转发失败
        Txt72, // 当前日期暂无记录
        Txt73, // 图表输入提示
        Txt74, // 图表输出提示
        Txt75, // 图表缓存命中提示
        Txt76, // 图表花费提示
        Txt77, // 输出摘要
        Txt78, // 未配置模型价格
        Txt79, // 输入 token 已分开上报
        Txt80, // 普通输入计算说明
        Txt81, // 模型提示
        Txt82, // 普通输入成本公式
        Txt83, // 输出成本公式
        Txt84, // 缓存命中成本公式
        Txt85, // 缓存创建成本公式
        Txt86, // 总计
        Txt87, // 读取请求记录失败
        Txt88, // 读取今日悬浮统计失败
        Txt89, // 读取统计汇总失败
        Txt90, // 保存请求记录失败
        Txt91, // 读取配置失败
        Txt92, // 读取模型价格配置失败
        Txt93, // 保存模型价格配置失败
        Txt94, // 保存配置失败
        Txt95, // 代理配置尚未初始化
        Txt96, // 未配置模型列表接口
        Txt97, // 模型列表接口无效
        Txt98, // 未配置供应商地址
        Txt99, // 供应商 API URL 无效
        Txt100, // 供应商 API URL 必须是最终接口地址
        Txt101, // 配置模型 Token 成本
        Txt102, // 添加
        Txt103, // 模型成本配置提示
        Txt104, // 模型名称
        Txt105, // 输入成本
        Txt106, // 输出成本
        Txt107, // 缓存命中成本
        Txt108, // 缓存创建成本
        Txt109, // 编辑
        Txt110, // 删除
        Txt111, // 保存
        Txt112, // 取消
        Txt113, // 价格无效说明
        Txt114, // 价格无效
        Txt115, // 配置供应商路由
        Txt116, // 自动推导模型列表接口
        Txt117, // 供应商 API URL
        Txt118, // 模型列表接口
        Txt119, // Anthropic 版本
        Txt120, // 供应商 API URL 必须是最终接口地址示例
        Txt121, // Responses 示例
        Txt122, // Chat Completions 示例
        Txt123, // Anthropic 示例
        Txt124, // 通用 URL 示例
        Txt125, // 今日统计窗标题
        Txt126, // 今日 Token
        Txt127, // 派发窗口标题：Provider Routes Save 等复用
    }

    internal static class AppTexts
    {
        private static readonly Dictionary<TextId, (string English, string Chinese)> Texts = new()
        {
            [TextId.Txt1] = ("Provider Settings", "配置供应商"),
            [TextId.Txt2] = ("Model Prices", "配置模型价格"),
            [TextId.Txt3] = ("Clear Current Date", "清空当前日期统计"),
            [TextId.Txt4] = ("Clear All Stats", "清空全部统计"),
            [TextId.Txt5] = ("Language:", "语言："),
            [TextId.Txt6] = ("Relay Settings", "代理配置"),
            [TextId.Txt7] = ("Local Listen URL", "本地监听地址"),
            [TextId.Txt8] = ("Route URL", "调用地址"),
            [TextId.Txt9] = ("To server", "发往服务器"),
            [TextId.Txt10] = ("From tool", "发给工具"),
            [TextId.Txt11] = ("Copy", "复制"),
            [TextId.Txt12] = ("Start relay on launch", "启动时自动启动代理"),
            [TextId.Txt13] = ("Open Log", "打开日志"),
            [TextId.Txt14] = ("Start Relay", "启动代理"),
            [TextId.Txt15] = ("Stop Relay", "停止代理"),
            [TextId.Txt16] = ("Save Settings", "保存配置"),
            [TextId.Txt17] = ("Status:", "状态："),
            [TextId.Txt18] = ("Token Usage", "Token 使用统计"),
            [TextId.Txt19] = ("Input", "输入"),
            [TextId.Txt20] = ("Output", "输出"),
            [TextId.Txt21] = ("Cache Hit", "缓存命中"),
            [TextId.Txt22] = ("Cost", "花费"),
            [TextId.Txt23] = ("Current Date Half-Hour Trend (input/output/cache/cost)", "当前日期半小时趋势（输入/输出/缓存命中/花费）"),
            [TextId.Txt24] = ("Record Date:", "记录日期："),
            [TextId.Txt25] = ("Time", "时间"),
            [TextId.Txt26] = ("Model", "模型"),
            [TextId.Txt27] = ("Path", "路径"),
            [TextId.Txt28] = ("Input/Cache Read", "输入/缓存读取"),
            [TextId.Txt29] = ("Time/First Byte", "用时/首字"),
            [TextId.Txt30] = ("Stats: All Dates", "统计：全部日期"),
            [TextId.Txt31] = ("Stats: Current Date", "统计：当前日期"),
            [TextId.Txt32] = ("Running", "运行中"),
            [TextId.Txt33] = ("Stopped", "已停止"),
            [TextId.Txt34] = ("None / no conversion", "空/不转换"),
            [TextId.Txt35] = ("Settings saved.", "配置已保存。"),
            [TextId.Txt36] = ("Route URL copied: {0}", "调用地址已复制：{0}"),
            [TextId.Txt37] = ("Failed to copy route URL: {0}", "复制调用地址失败：{0}"),
            [TextId.Txt38] = ("Copy Failed", "复制失败"),
            [TextId.Txt39] = ("Clear usage records for {0:yyyy-MM-dd}? This cannot be undone.", "确定要清空 {0:yyyy-MM-dd} 的统计记录吗？此操作不可恢复。"),
            [TextId.Txt40] = ("Clear Current Date", "确认清空当前日期"),
            [TextId.Txt41] = ("Usage and request records for {0:yyyy-MM-dd} have been cleared.", "{0:yyyy-MM-dd} 的统计和请求记录已清空。"),
            [TextId.Txt42] = ("Clear usage records for all dates? This cannot be undone.", "确定要清空全部日期的统计记录吗？此操作不可恢复。"),
            [TextId.Txt43] = ("Clear All Stats", "确认清空全部统计"),
            [TextId.Txt44] = ("Failed to delete record: {0}, {1}", "删除记录失败：{0}，{1}"),
            [TextId.Txt45] = ("Usage and request records for all dates have been cleared.", "全部日期的统计和请求记录已清空。"),
            [TextId.Txt46] = ("Failed to open log: {0}", "打开日志失败：{0}"),
            [TextId.Txt47] = ("Open Failed", "打开失败"),
            [TextId.Txt48] = ("Model price settings saved.", "模型价格配置已保存。"),
            [TextId.Txt49] = ("Provider settings cannot be changed while the relay is running. Stop the listener first to avoid inconsistent provider URL, model list, or version settings during active requests.", "代理正在运行时不能修改供应商配置。请先停止监听，避免正在转发的请求读取到不一致的供应商地址、模型列表接口或版本配置。"),
            [TextId.Txt50] = ("Cannot Modify While Running", "运行中不可修改"),
            [TextId.Txt51] = ("Provider settings saved.", "供应商配置已保存。"),
            [TextId.Txt52] = ("Hide Floating Stats", "隐藏悬浮统计"),
            [TextId.Txt53] = ("Open Main Window", "打开主窗口"),
            [TextId.Txt54] = ("Exit", "退出"),
            [TextId.Txt55] = ("APIRelay is running", "APIRelay 正在运行"),
            [TextId.Txt56] = ("Show Floating Stats", "显示悬浮统计"),
            [TextId.Txt57] = ("The local listen address must use http://127.0.0.1:port/ or http://localhost:port/.", "本地监听地址必须是 http://127.0.0.1:端口/ 或 http://localhost:端口/ 格式。"),
            [TextId.Txt58] = ("Invalid Address", "地址无效"),
            [TextId.Txt59] = ("Listening started: {0}", "已启动监听：{0}"),
            [TextId.Txt60] = ("Provider routes: /compatible, /responses, /anthropic. Append a /from protocol when conversion is needed.", "供应商路由：/compatible、/responses、/anthropic，可追加 /from 协议"),
            [TextId.Txt61] = ("Internal log: {0}", "内部日志：{0}"),
            [TextId.Txt62] = ("Failed to start listener: {0}\r\n\r\nIf the port is already in use, choose another port.", "启动监听失败：{0}\r\n\r\n如果端口被占用，请换一个端口。"),
            [TextId.Txt63] = ("Start Failed", "启动失败"),
            [TextId.Txt64] = ("Relay service error: {0}", "代理服务异常：{0}"),
            [TextId.Txt65] = ("Runtime Error", "运行异常"),
            [TextId.Txt66] = ("Request rejected: missing client API key.", "请求被拒绝：客户端 API Key 缺失。"),
            [TextId.Txt67] = ("Request: {0}", "请求：{0}"),
            [TextId.Txt68] = ("Response complete: {0}", "回复完成：{0}"),
            [TextId.Txt69] = ("Request canceled.", "请求已取消。"),
            [TextId.Txt70] = ("APIRelay forwarding failed: {0}", "APIRelay 转发失败：{0}"),
            [TextId.Txt71] = ("Forwarding failed: {0}", "转发失败：{0}"),
            [TextId.Txt72] = ("No token usage records for the current date", "当前日期暂无 token 使用记录"),
            [TextId.Txt73] = ("Input: {0:N0}", "输入: {0:N0}"),
            [TextId.Txt74] = ("Output: {0:N0}", "输出: {0:N0}"),
            [TextId.Txt75] = ("Cache Hit: {0:N0}", "缓存命中: {0:N0}"),
            [TextId.Txt76] = ("Cost: {0}", "花费: {0}"),
            [TextId.Txt77] = ("{0} {1} output {2}", "{0} {1} 输出{2}"),
            [TextId.Txt78] = ("Model: {0}\r\nNo model price is configured. Cost is counted as $0.000000.", "模型：{0}\r\n未配置模型价格，成本按 $0.000000 计算。"),
            [TextId.Txt79] = ("Input tokens are reported separately from cache hit/creation tokens", "输入 token 已与缓存命中/创建分开上报"),
            [TextId.Txt80] = ("Regular input = input - cache hit - cache creation", "普通输入 = 输入 - 缓存命中 - 缓存创建"),
            [TextId.Txt81] = ("Model: {0}", "模型：{0}"),
            [TextId.Txt82] = ("Regular input: {0:N0} x ${1:0.######}/million = {2}", "普通输入：{0:N0} x ${1:0.######}/百万 = {2}"),
            [TextId.Txt83] = ("Output: {0:N0} x ${1:0.######}/million = {2}", "输出：{0:N0} x ${1:0.######}/百万 = {2}"),
            [TextId.Txt84] = ("Cache hit: {0:N0} x ${1:0.######}/million = {2}", "缓存命中：{0:N0} x ${1:0.######}/百万 = {2}"),
            [TextId.Txt85] = ("Cache creation: {0:N0} x ${1:0.######}/million = {2}", "缓存创建：{0:N0} x ${1:0.######}/百万 = {2}"),
            [TextId.Txt86] = ("Total: {0}", "总计：{0}"),
            [TextId.Txt87] = ("Failed to read request records: {0}", "读取请求记录失败：{0}"),
            [TextId.Txt88] = ("Failed to read today's floating stats: {0}", "读取今日悬浮统计失败：{0}"),
            [TextId.Txt89] = ("Failed to read stats summary: {0}, {1}", "读取统计汇总失败：{0}，{1}"),
            [TextId.Txt90] = ("Failed to save request record: {0}", "保存请求记录失败：{0}"),
            [TextId.Txt91] = ("Failed to load settings: {0}", "读取配置失败：{0}"),
            [TextId.Txt92] = ("Failed to load model price settings: {0}", "读取模型价格配置失败：{0}"),
            [TextId.Txt93] = ("Failed to save model price settings: {0}", "保存模型价格配置失败：{0}"),
            [TextId.Txt94] = ("Failed to save settings: {0}", "保存配置失败：{0}"),
            [TextId.Txt95] = ("Relay settings have not been initialized.", "代理配置尚未初始化。"),
            [TextId.Txt96] = ("Model list URL is not configured for {0}.", "未配置 {0} 的模型列表接口。"),
            [TextId.Txt97] = ("{0} model list URL is invalid.", "{0} 的模型列表接口无效。"),
            [TextId.Txt98] = ("Provider URL is not configured for {0}.", "未配置 {0} 的供应商地址。"),
            [TextId.Txt99] = ("{0} provider API URL is invalid.", "{0} 的供应商 API URL 无效。"),
            [TextId.Txt100] = ("{0} provider API URL must be the final endpoint, not a base URL.", "{0} 的供应商 API URL 必须配置为最终接口地址，而不是基础地址。"),
            [TextId.Txt101] = ("Model Token Costs", "配置模型 Token 成本"),
            [TextId.Txt102] = ("Add", "添加"),
            [TextId.Txt103] = ("Configure token costs per model (per million, USD).", "配置各模型的 token 成本（每百万，美元）。"),
            [TextId.Txt104] = ("Model Name", "模型名称"),
            [TextId.Txt105] = ("Input Cost ($)", "输入成本($)"),
            [TextId.Txt106] = ("Output Cost ($)", "输出成本($)"),
            [TextId.Txt107] = ("Cache Hit Cost ($)", "缓存命中成本($)"),
            [TextId.Txt108] = ("Cache Creation Cost ($)", "缓存创建成本($)"),
            [TextId.Txt109] = ("Edit", "编辑"),
            [TextId.Txt110] = ("Delete", "删除"),
            [TextId.Txt111] = ("Save", "保存"),
            [TextId.Txt112] = ("Cancel", "取消"),
            [TextId.Txt113] = ("Input, output, cache hit, and cache creation costs must be numbers greater than or equal to 0.", "输入成本、输出成本、缓存命中成本和缓存创建成本必须是大于等于 0 的数字。"),
            [TextId.Txt114] = ("Invalid Price", "价格无效"),
            [TextId.Txt115] = ("Provider Routes", "配置供应商路由"),
            [TextId.Txt116] = ("Derived from provider API URL", "自动从供应商 API URL 推导"),
            [TextId.Txt117] = ("Provider API URL", "供应商 API URL"),
            [TextId.Txt118] = ("Model List URL", "模型列表接口"),
            [TextId.Txt119] = ("Anthropic Version", "Anthropic 版本"),
            [TextId.Txt120] = ("{0} provider API URL must be the final endpoint, for example: {1}", "{0} 的供应商 API URL 必须是最终接口地址，例如：{1}"),
            [TextId.Txt121] = ("Example: https://api.openai.com/v1/responses", "例如：https://api.openai.com/v1/responses"),
            [TextId.Txt122] = ("Example: https://api.openai.com/v1/chat/completions", "例如：https://api.openai.com/v1/chat/completions"),
            [TextId.Txt123] = ("Example: https://api.anthropic.com/v1/messages", "例如：https://api.anthropic.com/v1/messages"),
            [TextId.Txt124] = ("Example: https://api.example.com/v1", "例如：https://api.example.com/v1"),
            [TextId.Txt125] = ("APIRelay Today's Stats", "APIRelay 今日统计"),
            [TextId.Txt126] = ("Today's Tokens", "今日 Token"),
            [TextId.Txt127] = ("Invalid URL", "地址无效"),
        };

        public static string GetText(AppLanguage language, TextId id, params object[] args)
        {
            var text = Texts.TryGetValue(id, out var value)
                ? (language == AppLanguage.Chinese ? value.Chinese : value.English)
                : id.ToString();

            return args.Length == 0
                ? text
                : string.Format(CultureInfo.InvariantCulture, text, args);
        }
    }
}
