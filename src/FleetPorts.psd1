# Phoenix LAI's lines from the edge-ops port registry (PORTS.md, approved 2026-10-25).
# server: exclusive (it holds the GPU and port 1234); an older running server is never stopped automatically.
# client: multi (the Phoenix Client and the VS Code client each take the next free port; never reused or stopped).
# api: the OpenAI-compatible API, fixed at LM Studio's 1234. dev: private test port (.claude\claude_tests\chat-e2e.ps1).
@{
    'phoenix-lai-api'    = @{ Start = 1234; Size = 1;  Policy = 'exclusive' }
    'phoenix-lai-server' = @{ Start = 8780; Size = 10; Policy = 'exclusive' }
    'phoenix-lai-client' = @{ Start = 8790; Size = 9;  Policy = 'multi' }
    'phoenix-lai-dev'    = @{ Start = 8799; Size = 1;  Policy = 'restart' }
}
