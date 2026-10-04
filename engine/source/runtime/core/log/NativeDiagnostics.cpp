#include "core/log/NativeDiagnostics.h"
#include <spdlog/sinks/base_sink.h>
#include <spdlog/logger.h>
#include <deque>
#include <iomanip>
#include <sstream>
#include <locale>
#include <algorithm>
#include <cstring>

namespace {
std::string Bound(spdlog::string_view_t text) {
    std::size_t size=std::min(text.size(),std::size_t{2048});
    if(size<text.size())while(size && (static_cast<unsigned char>(text[size])&0xc0)==0x80)--size;
    return {text.data(),size};
}
void Quote(std::ostream& s,const std::string& value) {
    s<<'"'; for(unsigned char ch:value) {
        if(ch=='"'||ch=='\\')s<<'\\'<<static_cast<char>(ch);
        else if(ch<32)s<<"\\u"<<std::hex<<std::setw(4)<<std::setfill('0')<<static_cast<int>(ch)<<std::dec;
        else s<<static_cast<char>(ch);
    } s<<'"';
}
struct Entry {std::uint64_t Sequence;std::string Code,Message;};
class Ring final:public spdlog::sinks::base_sink<std::mutex> {
    std::deque<Entry> entries;std::uint64_t sequence=0;
    void sink_it_(const spdlog::details::log_msg& msg) override {
        if(sequence==UINT64_MAX)return;
        auto text=Bound(msg.payload);auto split=text.find(':');
        Entry entry{++sequence,text.substr(0,split),split==std::string::npos?text:text.substr(split+1)};
        if(entries.size()==512)entries.pop_front();entries.push_back(std::move(entry));
    }
    void flush_() override {}
public:
    std::string Copy(std::uint64_t after,std::uint32_t maximum) {
        std::scoped_lock lock(mutex_);std::ostringstream s;s.imbue(std::locale::classic());
        const auto dropped=entries.empty()||after>=entries.front().Sequence-1?0:entries.front().Sequence-1-after;
        s<<"{\"schemaVersion\":1,\"dropped\":"<<dropped<<",\"events\":[";std::uint32_t count=0;
        for(const auto& e:entries)if(e.Sequence>after && count<maximum) {
            if(count++)s<<',';s<<"{\"sequence\":"<<e.Sequence<<",\"level\":\"info\",\"code\":";Quote(s,e.Code);
            s<<",\"message\":";Quote(s,e.Message);s<<'}';
        }
        s<<"]}";return s.str();
    }
};
std::shared_ptr<Ring> Sink() {static auto sink=std::make_shared<Ring>();return sink;}
}
namespace NcmaEngine {
void NativeDiagnostic(const char* code,const char* message) noexcept {
    // Private logger, no stdout sink or global registry mutation (MCP stdio must stay clean).
    try {static spdlog::logger logger("NcmaNative",Sink());logger.info("{}:{}",code,message);}catch(...){}
}
}
extern "C" std::uint8_t NCMA_NATIVE_CALL ncma_diagnostics_read_v1(std::uint64_t after,std::uint32_t maximum,
    char* output,std::uint32_t capacity,std::uint32_t* required) {
    try {
        if(!required||maximum<1||maximum>32)return 0;
        const auto text=Sink()->Copy(after,maximum);
        if(text.size()+1>262144)return 0;
        *required=static_cast<std::uint32_t>(text.size()+1);if(capacity<*required)return 2;
        if(!output)return 0;std::memcpy(output,text.c_str(),*required);return 1;
    }catch(...){return 0;}
}
