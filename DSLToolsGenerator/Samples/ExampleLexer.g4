lexer grammar ExampleLexer;
options { language = CSharp; }

tokens { STRING_LITERAL }
channels { WHITESPACE, COMMENTS }


STR_DOUBLE : '"' ~[\r\n]*? '"' -> type(STRING_LITERAL) ;
CHAR_LITERAL : '\'' . '\'' ;

FUNCTION : 'function' ;
VAR : 'var' ;
PUBLIC : 'public' ;
RETURN : 'return' ;
AND : 'and' ;
OR : 'or' ;
NOT : 'not' ;
EQ : '=' ;
IF : 'if' ;
ELIF : 'elif' ;
ELSE : 'else' ;

COMMA : ',' ;
SEMI : ';' ;
LPAREN : '(' ;
RPAREN : ')' ;
LCURLY : '{' ;
RCURLY : '}' ;
PLUS : '+' ;

INT : [0-9]+ ;
ID : [a-zA-Z_][a-zA-Z_0-9]* ;
SPACES : [ ]+ -> channel(WHITESPACE) ;
TABS : '\t'+ -> channel(WHITESPACE) ;
NEWLINE : '\r'? '\n' -> channel(WHITESPACE) ;
COMMENT : '//' ~[\r\n]* -> channel(COMMENTS) ;
