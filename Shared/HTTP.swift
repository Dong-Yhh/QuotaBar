import Foundation

enum QuotaError: LocalizedError {
    case missingCredential(String)
    case badURL(String)
    case http(status: Int, message: String)
    case decoding(String)
    case noValue

    var errorDescription: String? {
        switch self {
        case .missingCredential(let hint):
            return "还没有填写凭证：\(hint)"
        case .badURL(let text):
            return "接口地址不合法：\(text)"
        case .http(let status, let message):
            if !message.isEmpty {
                switch status {
                case 401, 403: return "\(message)（HTTP \(status)）"
                case 429: return "\(message)（HTTP 429）"
                default: return "HTTP \(status) · \(message)"
                }
            }
            switch status {
            case 401, 403: return "凭证无效或权限不足（HTTP \(status)）"
            case 429: return "请求过于频繁（HTTP 429）"
            default: return "HTTP \(status)"
            }
        case .decoding(let text):
            return "解析返回内容失败：\(text)"
        case .noValue:
            return "接口返回里没有找到需要的数据，请检查字段路径"
        }
    }
}

struct HTTPResponse {
    var status: Int
    var object: Any?
    var text: String
    /// 键统一小写，方便查找。
    var headers: [String: String]

    func header(_ name: String) -> String? {
        headers[name.lowercased()]
    }
}

enum HTTPClient {
    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 20
        configuration.timeoutIntervalForResource = 30
        configuration.waitsForConnectivity = false
        configuration.httpAdditionalHeaders = ["User-Agent": "QuotaBar/1.0 (macOS)"]
        return URLSession(configuration: configuration)
    }()

    static func request(
        _ urlString: String,
        method: String = "GET",
        headers: [String: String] = [:],
        body: Data? = nil
    ) async throws -> HTTPResponse {
        guard let url = URL(string: urlString), url.scheme != nil else {
            throw QuotaError.badURL(urlString)
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.httpBody = body
        for (key, value) in headers {
            request.setValue(value, forHTTPHeaderField: key)
        }

        let (data, response) = try await session.data(for: request)
        let http = response as? HTTPURLResponse
        let status = http?.statusCode ?? 0
        var headerMap: [String: String] = [:]
        http?.allHeaderFields.forEach { key, value in
            if let key = key as? String {
                headerMap[key.lowercased()] = String(describing: value)
            }
        }
        let text = String(data: data, encoding: .utf8) ?? ""
        let object = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])

        return HTTPResponse(status: status, object: object, text: text, headers: headerMap)
    }

    static func json(
        _ urlString: String,
        method: String = "GET",
        headers: [String: String] = [:],
        body: Data? = nil
    ) async throws -> HTTPResponse {
        let response = try await request(urlString, method: method, headers: headers, body: body)
        guard (200..<300).contains(response.status) else {
            throw QuotaError.http(status: response.status, message: serverMessage(from: response))
        }
        return response
    }

    /// 服务端如果给了结构化的错误说明（例如 SoloAPI 的
    /// `{"code":"INVALID_API_KEY","message":"API Key 无效…"}`），
    /// 就直接用这句话，比丢一段截断的 JSON 有用得多。
    static func serverMessage(from response: HTTPResponse) -> String {
        if let object = response.object {
            for path in ["message", "error.message", "error_description", "detail", "error"] {
                if let text = JSONPath.string(in: object, path: path)?
                    .trimmingCharacters(in: .whitespacesAndNewlines),
                   !text.isEmpty {
                    return text
                }
            }
        }
        return shorten(response.text)
    }

    static func shorten(_ text: String, limit: Int = 180) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count > limit else { return trimmed }
        return String(trimmed.prefix(limit)) + "…"
    }
}
