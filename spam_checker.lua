-- Unified spam checker plugin for tg-spam
-- Calls the PreFilterApi for both text and image spam detection
-- Deploy this file to your tg-spam server's lua plugins directory

function check(req)
  local payload = {
    message = req.msg,
    user_id = tostring(req.user_id),
    user_name = req.user_name or "",
    chat_id = tostring(req.chat_id),
    image_count = req.meta.images or 0
  }

  local json_payload, err = json_encode(payload)
  if err then
    return false, "json encode error: " .. err
  end

  local response, status, net_err = http_request(
    "http://prefilter:5161/check",
    "POST",
    { ["Content-Type"] = "application/json" },
    json_payload,
    60  -- 60 second timeout
  )

  if net_err then
    return false, "http error: " .. net_err
  end

  if status ~= 200 then
    return false, "api error: status " .. status
  end

  local result, parse_err = json_decode(response)
  if parse_err then
    return false, "json parse error: " .. parse_err
  end

  if result.spam == true then
    local reason = result.reason or "blocked by API"
    if result.confidence and result.confidence > 0 then
      reason = reason .. " (confidence: " .. tostring(result.confidence) .. ")"
    end
    return true, reason
  end

  return false, result.reason or "no spam detected"
end
