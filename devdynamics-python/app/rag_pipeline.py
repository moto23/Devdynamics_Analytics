"""Optional LangChain + ChromaDB RAG pipeline; requires extra dependencies."""
from .config import Settings
from .context import records_from_snapshot


class RepositoryRAG:
    def __init__(self, settings: Settings | None = None):
        import os
        if not os.getenv("OPENAI_API_KEY"):
            raise RuntimeError("Set OPENAI_API_KEY to enable embeddings and grounded answers")
        from langchain_chroma import Chroma
        from langchain_openai import OpenAIEmbeddings

        self.settings = settings or Settings()
        self.store = Chroma(
            collection_name="devdynamics_repository_context",
            persist_directory=self.settings.chroma_directory,
            embedding_function=OpenAIEmbeddings(model=self.settings.embedding_model),
        )

    def index(self, snapshot: dict) -> int:
        from langchain_core.documents import Document
        records = records_from_snapshot(snapshot)
        if not records:
            return 0
        documents = [Document(page_content=r["text"], metadata=r["metadata"]) for r in records]
        self.store.add_documents(documents=documents, ids=[r["id"] for r in records])
        return len(documents)

    def ask(self, question: str, repository: str, top_k: int = 4) -> dict:
        from langchain_core.output_parsers import StrOutputParser
        from langchain_core.prompts import ChatPromptTemplate
        from langchain_openai import ChatOpenAI
        if not 1 <= top_k <= 10:
            raise ValueError("top_k must be between 1 and 10")
        matches = self.store.similarity_search(
            question, k=top_k, filter={"repository": repository}
        )
        if not matches:
            return {"question": question, "answer": "No indexed evidence was found for this repository.",
                    "retrieval_context": [], "sources": []}

        context = "\n\n".join(
            f"[{i}] {doc.page_content}\nSource: {doc.metadata.get('source', '')}"
            for i, doc in enumerate(matches, 1)
        )
        prompt = ChatPromptTemplate.from_messages([
            ("system", "You answer questions about GitHub activity using ONLY the supplied evidence. "
                       "If the evidence is insufficient, say so. Do not fabricate numbers. "
                       "Refer to source links when possible.\n\nEvidence:\n{context}"),
            ("human", "{question}"),
        ])
        chain = prompt | ChatOpenAI(model=self.settings.openai_model, temperature=0) | StrOutputParser()
        answer = chain.invoke({"question": question, "context": context})
        sources = list(dict.fromkeys(d.metadata.get("source", "") for d in matches if d.metadata.get("source")))
        return {"question": question, "answer": answer,
                "retrieval_context": [d.page_content for d in matches], "sources": sources}
