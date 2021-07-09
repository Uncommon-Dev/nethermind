grammar DslGrammar;

tree: (expression)* ;
expression : sourceExpression | watchExpression | whereExpression | publishExpression | andCondition | orCondition ;
sourceExpression : SOURCE WORD ;
watchExpression : WATCH WORD ;
whereExpression : WHERE condition ;
publishExpression : PUBLISH PUBLISH_VALUE ( WORD | DIGIT ) ;
condition : WORD BOOLEAN_OPERATOR ( WORD | DIGIT | ADDRESS | BYTECODE ) ; 
andCondition : AND condition ;
orCondition : OR condition ;

BOOLEAN_OPERATOR : ARITHMETIC_SYMBOL | CONTAINS ;
ARITHMETIC_SYMBOL : '==' | '!=' | '<' | '>' | '<=' | '>=' | IS | NOT ;

SOURCE : 'SOURCE' ;
WATCH : 'WATCH' ;
WHERE : 'WHERE' ;
PUBLISH : 'PUBLISH' ;
AND : 'AND' ;
OR : 'OR' ;
CONTAINS : 'CONTAINS' ;
IS: 'IS' ;
NOT: 'NOT' ;

PUBLISH_VALUE : WEBSOCKETS | TELEGRAM | DISCORD ;
WEBSOCKETS : 'WebSockets' | 'webSockets' | 'websockets' | 'Websockets' ;
TELEGRAM : 'Telegram' | 'telegram' ;
DISCORD : 'Discord' | 'discord' ;

WORD : [a-zA-Z]+ ;
DIGIT : [0-9]+;
BYTECODE : [a-fA-F0-9]+ ;
ADDRESS : '0x'[a-fA-F0-9]* ;
WS : [ \t\r\n]+ -> skip ; // skip spaces, tabs, newlines
